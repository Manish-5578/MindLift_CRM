using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Common;
using MindLift.Infrastructure.Leads;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Settings;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using MindLift.Shared.Imports;

namespace MindLift.Infrastructure.Contacts;

/// <summary>Shared helpers for the contact and account services.</summary>
internal static class RecordHelpers
{
    public static async Task<FilterContext> FilterContextAsync(AppDbContext db, ITenantContext tenant, IClock clock, CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);
        return new FilterContext(TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId), clock.UtcNow, (DayOfWeek)settings.WeekStartDay, settings.FiscalYearStartMonth, tenant.UserId);
    }

    /// <summary>
    /// Refuses an owner who is not an active user of this workspace (users are filtered to the current workspace), so a record cannot be given
    /// to a made-up id, a deactivated user, or someone in another company. Called only when the owner changes, so a record whose owner
    /// has since been deactivated can still be edited.
    /// </summary>
    public static async Task EnsureOwnerAsync(AppDbContext db, Guid ownerId, CancellationToken cancellationToken)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == ownerId && u.IsActive, cancellationToken))
        {
            throw new ValidationException("ownerId", "Choose an active user in this workspace.");
        }
    }

    /// <summary>As above, and the new owner must be inside the caller's data scope for the module, so an Own-scoped user cannot hand records to anyone.</summary>
    public static async Task EnsureOwnerAsync(AppDbContext db, IDataScopeService scope, AppModule module, Guid ownerId, CancellationToken cancellationToken)
    {
        await EnsureOwnerAsync(db, ownerId, cancellationToken);
        var allowed = await scope.GetScopeUserIdsAsync(module, cancellationToken);
        if (allowed is not null && !allowed.Contains(ownerId))
        {
            throw new ForbiddenException("You can only assign records to people inside your data scope.", "owner_out_of_scope");
        }
    }

    /// <summary>Limits a query of owned records to the caller's data scope for a module.</summary>
    public static async Task<IQueryable<T>> InScopeAsync<T>(IQueryable<T> query, IDataScopeService scope, AppModule module, System.Linq.Expressions.Expression<Func<T, Guid>> owner, CancellationToken cancellationToken)
    {
        var allowed = await scope.GetScopeUserIdsAsync(module, cancellationToken);
        if (allowed is null)
        {
            return query;
        }

        var parameter = owner.Parameters[0];
        var contains = System.Linq.Expressions.Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)],
            System.Linq.Expressions.Expression.Constant(allowed.ToList()), owner.Body);
        return query.Where(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(contains, parameter));
    }

    public static async Task<Dictionary<Guid, string>> OwnerNamesAsync(AppDbContext db, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IReadOnlyDictionary<string, JsonElement> Custom(JsonDocument? document)
        => document is null ? new Dictionary<string, JsonElement>() : document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    public static JsonDocument? CustomDoc(IReadOnlyDictionary<string, JsonElement>? values)
        => values is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(values));

    public static ValidationException Invalid(string key, string message) => new(new Dictionary<string, string[]> { [key] = [message] });

    public static async Task<ImportResultDto> ImportAsync(
        string csv,
        IReadOnlyDictionary<string, string[]> aliases,
        string required,
        string requiredLabel,
        Func<Func<string, string?>, CancellationToken, Task<bool>> isDuplicate,
        Func<Func<string, string?>, CancellationToken, Task> create,
        CancellationToken cancellationToken)
    {
        var rows = CsvText.Parse(csv);
        if (rows.Count < 2)
        {
            throw Invalid("file", "The file needs a header row and at least one row.");
        }

        if (rows.Count - 1 > 5000)
        {
            throw new BusinessRuleException("import_too_large", "Import at most 5000 rows at a time.");
        }

        var columns = ImportColumns.Map(rows[0], aliases);
        if (columns[required] < 0)
        {
            throw Invalid("file", $"The file needs a '{requiredLabel}' column.");
        }

        int created = 0, skipped = 0, failed = 0;
        var errors = new List<string>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            string? Cell(string key) => columns.TryGetValue(key, out var index) && index >= 0 && index < row.Count ? row[index].Trim() : null;

            if (string.IsNullOrWhiteSpace(Cell(required)))
            {
                failed++;
                if (errors.Count < 20) errors.Add($"Row {i + 1}: {requiredLabel.ToLowerInvariant()} is missing.");
                continue;
            }

            if (await isDuplicate(Cell, cancellationToken))
            {
                skipped++;
                continue;
            }

            try
            {
                await create(Cell, cancellationToken);
                created++;
            }
            catch (ValidationException ex)
            {
                failed++;
                if (errors.Count < 20) errors.Add($"Row {i + 1}: {string.Join(" ", ex.Errors.Values.SelectMany(v => v))}");
            }
        }

        return new ImportResultDto(rows.Count - 1, created, skipped, failed, errors);
    }
}

/// <summary>Contact records: list, read, create, edit, delete, duplicates, merge, import, export and the contact summary.</summary>
public sealed class ContactService(AppDbContext db, ITenantContext tenant, IClock clock, FieldSettingsService fields, IDataScopeService scope, INotificationService notifications, IRecordSavePipeline savePipeline,
    MindLift.Infrastructure.Common.BulkQueue bulkQueue, IPermissionService permissions, ITimelineEventBus timeline, IFeatureFlags featureFlags)
{
    private const int MaxExportRows = 10000;

    public static readonly IReadOnlyDictionary<string, string[]> ImportAliases = MindLift.Shared.Imports.ImportFieldSets.Contacts.ToDictionary(f => f.Field, f => f.Aliases);

    public async Task<PagedResult<ContactListItemDto>> QueryAsync(ListQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var source = await MatchingAsync(query, cancellationToken);

        var total = await source.CountAsync(cancellationToken);
        source = Sort(source, query.SortField, query.SortDir);
        var rows = await source.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var owners = await RecordHelpers.OwnerNamesAsync(db, rows.Select(r => r.OwnerId).Distinct(), cancellationToken);
        var accountIds = rows.Where(r => r.AccountId != null).Select(r => r.AccountId!.Value).Distinct().ToList();
        var visibleAccounts = await RecordHelpers.InScopeAsync(db.Accounts.AsNoTracking(), scope, AppModule.Accounts, a => a.OwnerId, cancellationToken);
        var accounts = await visibleAccounts.Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        var items = rows.Select(c => new ContactListItemDto(c.Id, $"{c.FirstName} {c.LastName}".Trim(), c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle,
            c.AccountId, c.AccountId is { } a ? accounts.GetValueOrDefault(a) : null, c.Type, c.OwnerId, owners.GetValueOrDefault(c.OwnerId), c.CreatedAt)).ToList();
        return new PagedResult<ContactListItemDto>(items, total, page, pageSize);
    }

    /// <summary>The contacts a list query matches (the user's scope, filters, search and any chosen ids), before sorting and paging.</summary>
    private async Task<IQueryable<Contact>> MatchingAsync(ListQuery query, CancellationToken cancellationToken)
    {
        var source = await ScopedAsync(cancellationToken);
        if (query.Ids is { Count: > 0 } ids)
        {
            source = source.Where(r => ids.Contains(r.Id));
        }

        if (query.Filter is not null)
        {
            var registry = await fields.RegistryAsync("contacts", cancellationToken);
            source = source.Where(FilterCompiler.Compile<Contact>(query.Filter, registry, await RecordHelpers.FilterContextAsync(db, tenant, clock, cancellationToken)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(c => (c.FirstName + " " + c.LastName).ToLower().Contains(term)
                || (c.Email != null && c.Email.ToLower().Contains(term))
                || (c.Phone != null && c.Phone.Contains(term))
                || (c.JobTitle != null && c.JobTitle.ToLower().Contains(term)));
        }

        return source;
    }

    /// <summary>The ids of the contacts a list query matches, at most <paramref name="limit"/>, for "select all" in bulk actions.</summary>
    public async Task<IReadOnlyList<Guid>> MatchingIdsAsync(ListQuery query, int limit, CancellationToken cancellationToken = default)
        => await (await MatchingAsync(query, cancellationToken)).OrderBy(r => r.Id).Select(r => r.Id).Take(limit).ToListAsync(cancellationToken);

    /// <summary>Assign many contacts to one owner. Each is saved as if edited on its own page.</summary>
    public async Task<BulkActionResult> BulkAsync(BulkActionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Action != BulkActions.Assign)
        {
            throw new ValidationException("action", $"'{request.Action}' is not something that can be done to many contacts at once.");
        }

        if (request.OwnerId is not { } owner)
        {
            throw new ValidationException("ownerId", "Choose who to assign the contacts to.");
        }

        var ids = await BulkRunner.TargetsAsync(request.Ids, request.AllMatching, MatchingIdsAsync, cancellationToken);
        if (bulkQueue.ShouldQueue(ids))
        {
            return bulkQueue.Queue("contacts", request, ids);
        }

        return await BulkRunner.RunAsync(db, ids, async (id, ct) =>
        {
            var current = await GetAsync(id, ct);
            await UpdateAsync(id, ToSave(current) with { OwnerId = owner }, ct);
        }, cancellationToken);
    }

    public async Task<BulkActionResult> BulkDeleteAsync(BulkDeleteRequest request, CancellationToken cancellationToken = default)
    {
        var ids = await BulkRunner.TargetsAsync(request.Ids, request.AllMatching, MatchingIdsAsync, cancellationToken);
        if (bulkQueue.ShouldQueue(ids))
        {
            return bulkQueue.QueueDelete("contacts", ids);
        }

        return await BulkRunner.RunAsync(db, ids, DeleteAsync, cancellationToken);
    }

    /// <summary>The record's current values as a save request, so a bulk change alters only the one thing it is about.</summary>
    private static SaveContactRequest ToSave(ContactDto c) => new(c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle, c.Description, c.AccountId, c.Type, c.OwnerId, c.CustomFields);

    public async Task<ContactDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await MapAsync(await LoadScopedAsync(id, cancellationToken), cancellationToken);

    public async Task<ContactSummaryDto> SummaryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var contact = await LoadScopedAsync(id, cancellationToken);
        var leads = await RecordHelpers.InScopeAsync(db.Leads.AsNoTracking(), scope, AppModule.Leads, l => l.OwnerId, cancellationToken);
        var lead = await leads.Where(l => l.ConvertedContactId == id).FirstOrDefaultAsync(cancellationToken);
        var converted = lead is null ? null : new ConvertedLeadDto(lead.Id, $"{lead.FirstName} {lead.LastName}".Trim(), lead.Source, lead.CreatedAt, lead.ConvertedAt!.Value);

        // Deals the contact takes part in, limited to the deals the caller may see.
        var scopedDeals = await RecordHelpers.InScopeAsync(db.Deals.AsNoTracking(), scope, AppModule.Deals, d => d.OwnerId, cancellationToken);
        var deals = await scopedDeals.Where(d => db.DealContacts.Any(l => l.DealId == d.Id && l.ContactId == id)).OrderByDescending(d => d.CreatedAt).Take(50)
                .Select(d => new DealLinkDto(d.Id, d.Name, d.Status, d.Amount, d.Currency)).ToListAsync(cancellationToken);
        var scopedActivities = await RecordHelpers.InScopeAsync(db.Activities.AsNoTracking(), scope, AppModule.Activities, a => a.OwnerId, cancellationToken);
        var activities = await scopedActivities.CountAsync(a => a.ContactId == id, cancellationToken);
        return new ContactSummaryDto(await MapAsync(contact, cancellationToken), converted, deals.Count, activities, deals);
    }

    public async Task<ContactDto> CreateAsync(SaveContactRequest request, CancellationToken cancellationToken = default)
    {
        var contact = new Contact { OwnerId = tenant.UserId ?? throw new ForbiddenException("Sign in to create records.", "no_user") };
        await ApplyAsync(contact, request, cancellationToken);
        contact = await savePipeline.ExecuteCreateAsync("contacts", contact, async () =>
        {
            db.Contacts.Add(contact);
            await db.SaveChangesAsync(cancellationToken);
            return contact;
        }, cancellationToken);
        await NotifyOwnerAsync(contact.OwnerId, contact.Id, $"{contact.FirstName} {contact.LastName}".Trim(), cancellationToken);
        return await MapAsync(contact, cancellationToken);
    }

    public async Task<ContactDto> UpdateAsync(Guid id, SaveContactRequest request, CancellationToken cancellationToken = default)
    {
        var contact = await LoadScopedAsync(id, cancellationToken);
        var previousOwner = contact.OwnerId;
        var previousSnapshot = new Contact
        {
            Id = contact.Id,
            FirstName = contact.FirstName,
            LastName = contact.LastName,
            Email = contact.Email,
            Phone = contact.Phone,
            JobTitle = contact.JobTitle,
            AccountId = contact.AccountId,
            Type = contact.Type,
            OwnerId = contact.OwnerId,
            CustomFields = contact.CustomFields
        };

        await ApplyAsync(contact, request, cancellationToken);
        contact = await savePipeline.ExecuteUpdateAsync("contacts", contact, previousSnapshot, async () =>
        {
            await db.SaveChangesAsync(cancellationToken);
            return contact;
        }, cancellationToken);

        if (contact.AccountId != previousSnapshot.AccountId)
        {
            var linkedPortalUsers = await db.PortalUsers.Where(u => u.ContactId == contact.Id).ToListAsync(cancellationToken);
            foreach (var pu in linkedPortalUsers)
            {
                pu.AccountId = contact.AccountId;
                pu.SessionVersion++;
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        if (contact.OwnerId != previousOwner)
        {
            await NotifyOwnerAsync(contact.OwnerId, contact.Id, $"{contact.FirstName} {contact.LastName}".Trim(), cancellationToken);
        }

        return await MapAsync(contact, cancellationToken);
    }

    private Task NotifyOwnerAsync(Guid ownerId, Guid contactId, string name, CancellationToken cancellationToken)
        => notifications.NotifyAsync(new NotificationRequest([ownerId], NotificationTypes.ContactAssigned, $"Contact assigned to you: {name}", null, "contacts", contactId, $"/contacts/{contactId}"), cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var contact = await LoadScopedAsync(id, cancellationToken);
        db.Contacts.Remove(contact);

        var portalUsers = await db.PortalUsers.Where(u => u.ContactId == id).ToListAsync(cancellationToken);
        foreach (var pu in portalUsers)
        {
            pu.IsActive = false;
            pu.SessionVersion++;
            pu.PasswordChangedAt = clock.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DuplicateCandidateDto>> CheckDuplicatesAsync(CheckDuplicatesRequest request, CancellationToken cancellationToken = default)
    {
        var email = LeadNormalizer.Email(request.Email);
        var phone = LeadNormalizer.Phone(request.Phone);
        var contacts = await (await ScopedAsync(cancellationToken))
            .Where(c => (email != null && c.NormalizedEmail == email) || (phone != null && c.NormalizedPhone == phone))
            .ToListAsync(cancellationToken);
        return contacts.Select(c => new DuplicateCandidateDto(c.Id, "contact", $"{c.FirstName} {c.LastName}".Trim(), c.Email, c.Phone,
            email != null && c.NormalizedEmail == email ? "Same email" : "Same phone")).ToList();
    }

    public async Task<IReadOnlyList<DuplicateCandidateDto>> GetDuplicatesForContactAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        var contact = await LoadScopedAsync(contactId, cancellationToken);
        var checkRequest = new CheckDuplicatesRequest(contact.FirstName, contact.LastName, contact.Email, contact.Phone, null);
        var candidates = await CheckDuplicatesAsync(checkRequest, cancellationToken);
        return candidates.Where(c => c.Id != contactId).ToList();
    }

    public static bool IsDuplicateCandidate(Contact master, Contact other)
    {
        if (master.Id == other.Id) return false;
        var emailMatch = !string.IsNullOrWhiteSpace(master.NormalizedEmail) && master.NormalizedEmail == other.NormalizedEmail;
        var phoneMatch = !string.IsNullOrWhiteSpace(master.NormalizedPhone) && master.NormalizedPhone == other.NormalizedPhone;
        var nameMatch = !string.IsNullOrWhiteSpace(master.FirstName) && !string.IsNullOrWhiteSpace(master.LastName)
            && string.Equals(master.FirstName.Trim(), other.FirstName.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(master.LastName.Trim(), other.LastName.Trim(), StringComparison.OrdinalIgnoreCase);
        return emailMatch || phoneMatch || nameMatch;
    }

    /// <summary>Moves what is useful from the duplicates into the master, repoints their activities and all relations, and removes the duplicates (soft delete).</summary>
    public async Task<ContactDto> MergeAsync(
        Guid masterId,
        IReadOnlyList<Guid> duplicateIds,
        IReadOnlyDictionary<string, string?>? fieldOverrides = null,
        CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasAsync(AppModule.Contacts, PermissionAction.Edit, cancellationToken) ||
            !await permissions.HasAsync(AppModule.Contacts, PermissionAction.Delete, cancellationToken))
        {
            throw new ForbiddenException("Merging contacts requires both Edit and Delete permissions.", "forbidden");
        }

        if (duplicateIds == null || duplicateIds.Count == 0)
        {
            throw new ValidationException("duplicateIds", "At least one duplicate contact ID must be provided.");
        }

        if (duplicateIds.Contains(masterId))
        {
            throw new ValidationException("duplicateIds", "Master contact cannot be listed as a duplicate of itself.");
        }

        if (duplicateIds.Distinct().Count() != duplicateIds.Count)
        {
            throw new ValidationException("duplicateIds", "Duplicate contact IDs must be unique.");
        }

        if (fieldOverrides != null && fieldOverrides.Count > 0)
        {
            if (!await featureFlags.IsEnabledAsync("duplicate-merge", cancellationToken))
            {
                throw new ForbiddenException("Advanced side-by-side duplicate merge requires the 'duplicate-merge' feature flag.", "feature_disabled");
            }

            if (duplicateIds.Count > 2)
            {
                throw new ValidationException("duplicateIds", "Side-by-side merge allows merging at most 2 duplicate records into the master record (up to 3 records total).");
            }
        }

        var master = await LoadScopedAsync(masterId, cancellationToken);
        var requested = duplicateIds.ToList();
        var duplicates = await (await ScopedAsync(cancellationToken)).Where(c => requested.Contains(c.Id)).ToListAsync(cancellationToken);
        if (duplicates.Count != requested.Count)
        {
            throw new NotFoundException("Contact", requested.First(r => duplicates.All(d => d.Id != r)));
        }

        // Validate each duplicate against server-side duplicate-matching rules before merge
        foreach (var duplicate in duplicates)
        {
            if (!IsDuplicateCandidate(master, duplicate))
            {
                throw new ValidationException("duplicateIds", $"Contact '{duplicate.FirstName} {duplicate.LastName}' (ID: {duplicate.Id}) is not a duplicate of the master contact. Both records must share a matching normalized email, phone number, or full name.");
            }
        }

        // Apply explicit field overrides (from side-by-side comparison selection)
        if (fieldOverrides != null && fieldOverrides.Count > 0)
        {
            foreach (var (k, v) in fieldOverrides)
            {
                var key = k.Trim().ToLowerInvariant();
                switch (key)
                {
                    case "firstname" or "first_name":
                        if (!string.IsNullOrWhiteSpace(v)) master.FirstName = v.Trim();
                        break;
                    case "lastname" or "last_name":
                        if (!string.IsNullOrWhiteSpace(v)) master.LastName = v.Trim();
                        break;
                    case "email":
                        master.Email = v?.Trim();
                        master.NormalizedEmail = LeadNormalizer.Email(v);
                        break;
                    case "phone":
                        master.Phone = v?.Trim();
                        master.NormalizedPhone = LeadNormalizer.Phone(v);
                        break;
                    case "jobtitle" or "job_title" or "title":
                        master.JobTitle = v?.Trim();
                        break;
                    case "description":
                        master.Description = v?.Trim();
                        break;
                    case "accountid" or "account_id":
                        if (Guid.TryParse(v, out var parsedAccId)) master.AccountId = parsedAccId;
                        break;
                }
            }
        }

        foreach (var duplicate in duplicates)
        {
            master.Email ??= duplicate.Email;
            master.NormalizedEmail ??= duplicate.NormalizedEmail;
            master.Phone ??= duplicate.Phone;
            master.NormalizedPhone ??= duplicate.NormalizedPhone;
            master.JobTitle ??= duplicate.JobTitle;
            master.Description ??= duplicate.Description;
            master.AccountId ??= duplicate.AccountId;

            // Emit timeline events
            await timeline.PublishAsync(new TimelineEventInput(
                EntityType: "contact",
                EntityId: masterId,
                Kind: "contact_merged",
                Summary: $"Merged duplicate contact '{duplicate.FirstName} {duplicate.LastName}' into this contact",
                Source: "crm",
                DetailJson: JsonSerializer.Serialize(new { DuplicateId = duplicate.Id, duplicate.Email, duplicate.Phone, duplicate.JobTitle }),
                ActorUserId: tenant.UserId,
                OccurredAt: clock.UtcNow
            ), cancellationToken);

            await timeline.PublishAsync(new TimelineEventInput(
                EntityType: "contact",
                EntityId: duplicate.Id,
                Kind: "contact_merged_into",
                Summary: $"Merged into master contact '{master.FirstName} {master.LastName}' ({masterId})",
                Source: "crm",
                DetailJson: JsonSerializer.Serialize(new { MasterId = masterId }),
                ActorUserId: tenant.UserId,
                OccurredAt: clock.UtcNow
            ), cancellationToken);

            db.Contacts.Remove(duplicate);
        }

        // 1. Activities
        await db.Activities
            .Where(a => a.ContactId.HasValue && requested.Contains(a.ContactId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ContactId, masterId), cancellationToken);

        // 2. Leads (converted contact link)
        await db.Leads
            .Where(l => l.ConvertedContactId.HasValue && requested.Contains(l.ConvertedContactId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ConvertedContactId, masterId), cancellationToken);

        // 3. Deals (primary contact)
        await db.Deals
            .Where(d => d.PrimaryContactId.HasValue && requested.Contains(d.PrimaryContactId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.PrimaryContactId, masterId), cancellationToken);

        // 4. DealContacts (many-to-many: avoid composite key conflicts)
        var masterDealIds = await db.DealContacts
            .Where(dc => dc.ContactId == masterId)
            .Select(dc => dc.DealId)
            .ToListAsync(cancellationToken);

        var dupDealContacts = await db.DealContacts
            .Where(dc => requested.Contains(dc.ContactId))
            .ToListAsync(cancellationToken);

        foreach (var dc in dupDealContacts)
        {
            if (!masterDealIds.Contains(dc.DealId))
            {
                db.DealContacts.Add(new DealContact { DealId = dc.DealId, ContactId = masterId, Role = dc.Role, IsPrimary = dc.IsPrimary });
                masterDealIds.Add(dc.DealId);
            }
            db.DealContacts.Remove(dc);
        }

        // 5. ContactAccountLinks (many-to-many: avoid duplicate links)
        var masterAccountIds = await db.ContactAccountLinks
            .Where(l => l.ContactId == masterId)
            .Select(l => l.AccountId)
            .ToListAsync(cancellationToken);

        var dupAccountLinks = await db.ContactAccountLinks
            .Where(l => requested.Contains(l.ContactId))
            .ToListAsync(cancellationToken);

        foreach (var link in dupAccountLinks)
        {
            if (!masterAccountIds.Contains(link.AccountId))
            {
                db.ContactAccountLinks.Add(new ContactAccountLink
                {
                    ContactId = masterId,
                    AccountId = link.AccountId,
                    Role = link.Role
                });
                masterAccountIds.Add(link.AccountId);
            }
            db.ContactAccountLinks.Remove(link);
        }

        // 6. BusinessDocuments
        await db.BusinessDocuments
            .Where(d => d.AssociatedContactId.HasValue && requested.Contains(d.AssociatedContactId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.AssociatedContactId, masterId), cancellationToken);

        // 7. Messages
        await db.Messages
            .Where(m => m.ContactId.HasValue && requested.Contains(m.ContactId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ContactId, masterId), cancellationToken);

        // 8. Bookings
        await db.Bookings
            .Where(b => b.ContactId.HasValue && requested.Contains(b.ContactId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ContactId, masterId), cancellationToken);

        // 9. Tickets
        await db.Tickets
            .Where(t => t.ContactId.HasValue && requested.Contains(t.ContactId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ContactId, masterId), cancellationToken);

        // 10. Quotes
        await db.Quotes
            .Where(q => q.ContactId.HasValue && requested.Contains(q.ContactId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.ContactId, masterId), cancellationToken);

        // 11. Invoices
        await db.Invoices
            .Where(i => i.ContactId.HasValue && requested.Contains(i.ContactId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ContactId, masterId), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await MapAsync(master, cancellationToken);
    }

    public Task<ImportResultDto> ImportCsvAsync(string csv, CancellationToken cancellationToken = default)
        => RecordHelpers.ImportAsync(csv, ImportAliases, "first_name", "First name",
            async (cell, ct) =>
            {
                var email = LeadNormalizer.Email(cell("email"));
                var phone = LeadNormalizer.Phone(cell("phone"));
                return (email is not null && await db.Contacts.AnyAsync(c => c.NormalizedEmail == email, ct))
                    || (phone is not null && await db.Contacts.AnyAsync(c => c.NormalizedPhone == phone, ct));
            },
            async (cell, ct) =>
            {
                Guid? accountId = null;
                if (cell("account") is { Length: > 0 } accountName)
                {
                    var lowered = accountName.ToLower();
                    accountId = await db.Accounts.Where(a => a.Name.ToLower() == lowered).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
                }

                await CreateAsync(new SaveContactRequest(cell("first_name")!, cell("last_name") ?? string.Empty, cell("email"), cell("phone"), cell("job_title"), null, accountId, null, null, null), ct);
            },
            cancellationToken);

    public async Task<string> ExportCsvAsync(ListQuery query, CancellationToken cancellationToken = default)
    {
        const int pageSize = 200;
        var items = new List<ContactListItemDto>();
        for (var pageNumber = 1; items.Count < MaxExportRows; pageNumber++)
        {
            var page = await QueryAsync(query with { Page = pageNumber, PageSize = pageSize }, cancellationToken);
            items.AddRange(page.Items);
            if (page.Items.Count < pageSize || items.Count >= page.Total) break;
        }

        var builder = new StringBuilder("id,first_name,last_name,email,phone,job_title,account,type,owner,created_at\n");
        foreach (var c in items.Take(MaxExportRows))
        {
            var parts = new[] { c.Id.ToString(), c.FirstName, c.LastName, c.Email ?? "", c.Phone ?? "", c.JobTitle ?? "", c.AccountName ?? "", c.Type ?? "", c.OwnerName ?? "", c.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture) };
            builder.AppendLine(string.Join(",", parts.Select(CsvText.Escape)));
        }

        return builder.ToString();
    }

    private async Task ApplyAsync(Contact contact, SaveContactRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            throw RecordHelpers.Invalid("firstName", "First name is required.");
        }

        if (request.Type is not null && !await db.PicklistValues.AnyAsync(p => p.PicklistKey == "contact_type" && p.Value == request.Type && p.IsActive, cancellationToken))
        {
            throw RecordHelpers.Invalid("type", "Choose a valid type.");
        }

        if (request.AccountId is { } accountId
            && !await (await RecordHelpers.InScopeAsync(db.Accounts.AsNoTracking(), scope, AppModule.Accounts, a => a.OwnerId, cancellationToken)).AnyAsync(a => a.Id == accountId, cancellationToken))
        {
            throw RecordHelpers.Invalid("accountId", "Choose an existing account.");
        }

        var customErrors = CustomFieldRules.ValidateValues(await fields.CustomRulesAsync("contacts", cancellationToken),
            request.CustomFields?.ToDictionary(kv => kv.Key, kv => (JsonElement?)kv.Value) ?? []);
        if (customErrors.Count > 0)
        {
            throw new ValidationException(customErrors);
        }

        contact.FirstName = request.FirstName.Trim();
        contact.LastName = request.LastName.Trim();
        contact.Email = RecordHelpers.Clean(request.Email);
        contact.NormalizedEmail = LeadNormalizer.Email(request.Email);
        contact.Phone = RecordHelpers.Clean(request.Phone);
        contact.NormalizedPhone = LeadNormalizer.Phone(request.Phone);
        contact.JobTitle = RecordHelpers.Clean(request.JobTitle);
        contact.Description = request.Description;
        contact.AccountId = request.AccountId;
        contact.Type = request.Type ?? contact.Type;
        if (request.OwnerId is { } owner)
        {
            if (owner != contact.OwnerId)
            {
                await RecordHelpers.EnsureOwnerAsync(db, scope, AppModule.Contacts, owner, cancellationToken);
            }

            contact.OwnerId = owner;
        }

        if (request.CustomFields is not null)
        {
            contact.CustomFields = RecordHelpers.CustomDoc(request.CustomFields);
        }
    }

    private async Task<ContactDto> MapAsync(Contact c, CancellationToken cancellationToken)
    {
        var visibleAccounts = await RecordHelpers.InScopeAsync(db.Accounts.AsNoTracking(), scope, AppModule.Accounts, a => a.OwnerId, cancellationToken);
        var accountName = c.AccountId is { } id ? await visibleAccounts.Where(a => a.Id == id).Select(a => a.Name).FirstOrDefaultAsync(cancellationToken) : null;
        return new ContactDto(c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle, c.Description, c.AccountId, accountName, c.Type, c.OwnerId, c.CreatedAt, RecordHelpers.Custom(c.CustomFields));
    }

    private async Task<IQueryable<Contact>> ScopedAsync(CancellationToken cancellationToken)
    {
        var ids = await scope.GetScopeUserIdsAsync(AppModule.Contacts, cancellationToken);
        var query = db.Contacts.AsQueryable();
        return ids is null ? query : query.Where(c => ids.Contains(c.OwnerId));
    }

    private async Task<Contact> LoadScopedAsync(Guid id, CancellationToken cancellationToken)
        => await (await ScopedAsync(cancellationToken)).FirstOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new NotFoundException("Contact", id);

    private static IQueryable<Contact> Sort(IQueryable<Contact> query, string? field, string? dir)
    {
        var descending = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
        Expression<Func<Contact, object>> key = field switch
        {
            "first_name" => c => c.FirstName,
            "last_name" => c => c.LastName,
            "email" => c => c.Email!,
            "job_title" => c => c.JobTitle!,
            _ => c => c.CreatedAt,
        };
        return descending ? query.OrderByDescending(key).ThenBy(c => c.Id) : query.OrderBy(key).ThenBy(c => c.Id);
    }
}

/// <summary>Account (company) records, the parent hierarchy and the account summary.</summary>
public sealed class AccountService(AppDbContext db, ITenantContext tenant, IClock clock, FieldSettingsService fields, IDataScopeService scope, INotificationService notifications, IRecordSavePipeline savePipeline,
    MindLift.Infrastructure.Common.BulkQueue bulkQueue)
{
    public static readonly IReadOnlyDictionary<string, string[]> ImportAliases = MindLift.Shared.Imports.ImportFieldSets.Accounts.ToDictionary(f => f.Field, f => f.Aliases);

    public async Task<PagedResult<AccountListItemDto>> QueryAsync(ListQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var source = await MatchingAsync(query, cancellationToken);

        var total = await source.CountAsync(cancellationToken);
        var descending = !string.Equals(query.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        Expression<Func<Account, object>> key = query.SortField switch { "name" => a => a.Name, "industry" => a => a.Industry!, "website" => a => a.Website!, _ => a => a.CreatedAt };
        source = descending ? source.OrderByDescending(key).ThenBy(a => a.Id) : source.OrderBy(key).ThenBy(a => a.Id);
        var rows = await source.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.Contacts.AsNoTracking().Where(c => c.AccountId != null && ids.Contains(c.AccountId.Value))
            .GroupBy(c => c.AccountId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var parentIds = rows.Where(r => r.ParentAccountId != null).Select(r => r.ParentAccountId!.Value).Distinct().ToList();
        var scopedAccounts = await ScopedAsync(cancellationToken);
        var parents = await scopedAccounts.AsNoTracking().Where(a => parentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var owners = await RecordHelpers.OwnerNamesAsync(db, rows.Select(r => r.OwnerId).Distinct(), cancellationToken);

        var items = rows.Select(a => new AccountListItemDto(a.Id, a.Name, a.Website, a.Phone, a.Industry, a.ParentAccountId,
            a.ParentAccountId is { } p ? parents.GetValueOrDefault(p) : null, counts.GetValueOrDefault(a.Id), a.OwnerId, owners.GetValueOrDefault(a.OwnerId), a.CreatedAt)).ToList();
        return new PagedResult<AccountListItemDto>(items, total, page, pageSize);
    }

    /// <summary>The accounts a list query matches (the user's scope, filters, search and any chosen ids), before sorting and paging.</summary>
    private async Task<IQueryable<Account>> MatchingAsync(ListQuery query, CancellationToken cancellationToken)
    {
        var source = await ScopedAsync(cancellationToken);
        if (query.Ids is { Count: > 0 } ids)
        {
            source = source.Where(r => ids.Contains(r.Id));
        }

        if (query.Filter is not null)
        {
            var registry = await fields.RegistryAsync("accounts", cancellationToken);
            source = source.Where(FilterCompiler.Compile<Account>(query.Filter, registry, await RecordHelpers.FilterContextAsync(db, tenant, clock, cancellationToken)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(a => a.Name.ToLower().Contains(term) || (a.Website != null && a.Website.ToLower().Contains(term))
                || (a.Industry != null && a.Industry.ToLower().Contains(term)) || (a.Phone != null && a.Phone.Contains(term)));
        }

        return source;
    }

    /// <summary>The ids of the accounts a list query matches, at most <paramref name="limit"/>, for "select all" in bulk actions.</summary>
    public async Task<IReadOnlyList<Guid>> MatchingIdsAsync(ListQuery query, int limit, CancellationToken cancellationToken = default)
        => await (await MatchingAsync(query, cancellationToken)).OrderBy(r => r.Id).Select(r => r.Id).Take(limit).ToListAsync(cancellationToken);

    /// <summary>Assign many accounts to one owner. Each is saved as if edited on its own page.</summary>
    public async Task<BulkActionResult> BulkAsync(BulkActionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Action != BulkActions.Assign)
        {
            throw new ValidationException("action", $"'{request.Action}' is not something that can be done to many accounts at once.");
        }

        if (request.OwnerId is not { } owner)
        {
            throw new ValidationException("ownerId", "Choose who to assign the accounts to.");
        }

        var ids = await BulkRunner.TargetsAsync(request.Ids, request.AllMatching, MatchingIdsAsync, cancellationToken);
        if (bulkQueue.ShouldQueue(ids))
        {
            return bulkQueue.Queue("accounts", request, ids);
        }

        return await BulkRunner.RunAsync(db, ids, async (id, ct) =>
        {
            var current = await GetAsync(id, ct);
            await UpdateAsync(id, ToSave(current) with { OwnerId = owner }, ct);
        }, cancellationToken);
    }

    public async Task<BulkActionResult> BulkDeleteAsync(BulkDeleteRequest request, CancellationToken cancellationToken = default)
    {
        var ids = await BulkRunner.TargetsAsync(request.Ids, request.AllMatching, MatchingIdsAsync, cancellationToken);
        if (bulkQueue.ShouldQueue(ids))
        {
            return bulkQueue.QueueDelete("accounts", ids);
        }

        return await BulkRunner.RunAsync(db, ids, DeleteAsync, cancellationToken);
    }

    /// <summary>The record's current values as a save request, so a bulk change alters only the one thing it is about.</summary>
    private static SaveAccountRequest ToSave(AccountDto a) => new(a.Name, a.Website, a.Phone, a.Industry, a.Description, a.ParentAccountId, a.OwnerId, a.CustomFields);

    public async Task<AccountDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await MapAsync(await LoadScopedAsync(id, cancellationToken), cancellationToken);

    public async Task<AccountSummaryDto> SummaryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await LoadScopedAsync(id, cancellationToken);
        var scopedContacts = await RecordHelpers.InScopeAsync(db.Contacts.AsNoTracking(), scope, AppModule.Contacts, c => c.OwnerId, cancellationToken);
        var primaryContacts = await scopedContacts.Where(c => c.AccountId == id).OrderBy(c => c.FirstName).Take(200)
            .Select(c => new AccountContactDto(c.Id, (c.FirstName + " " + c.LastName).Trim(), c.Email, c.Phone, c.JobTitle, null, true)).ToListAsync(cancellationToken);
        var secondaryContacts = await (from l in db.ContactAccountLinks.AsNoTracking()
                                       join c in scopedContacts on l.ContactId equals c.Id
                                       where l.AccountId == id
                                       orderby c.FirstName
                                       select new AccountContactDto(c.Id, (c.FirstName + " " + c.LastName).Trim(), c.Email, c.Phone, c.JobTitle, l.Role, false))
                                      .Take(200).ToListAsync(cancellationToken);
        var contacts = primaryContacts.Concat(secondaryContacts).ToList();
        var scopedAccounts = await ScopedAsync(cancellationToken);
        var children = await scopedAccounts.AsNoTracking().Where(a => a.ParentAccountId == id).OrderBy(a => a.Name).Select(a => new AccountChildDto(a.Id, a.Name)).ToListAsync(cancellationToken);
        var scopedDeals = await RecordHelpers.InScopeAsync(db.Deals.AsNoTracking(), scope, AppModule.Deals, d => d.OwnerId, cancellationToken);
        var deals = await scopedDeals.Where(d => d.AccountId == id).OrderByDescending(d => d.CreatedAt).Take(100)
            .Select(d => new DealLinkDto(d.Id, d.Name, d.Status, d.Amount, d.Currency)).ToListAsync(cancellationToken);
        var scopedActivities = await RecordHelpers.InScopeAsync(db.Activities.AsNoTracking(), scope, AppModule.Activities, a => a.OwnerId, cancellationToken);
        var activities = await scopedActivities.CountAsync(a => a.AccountId == id || (a.ContactId != null && db.Contacts.Any(c => c.Id == a.ContactId && c.AccountId == id)), cancellationToken);
        return new AccountSummaryDto(await MapAsync(account, cancellationToken), contacts, children, deals,
            deals.Where(d => d.Status == "Open").Sum(d => d.Amount), deals.Where(d => d.Status == "Won").Sum(d => d.Amount), activities);
    }

    public async Task<AccountDto> CreateAsync(SaveAccountRequest request, CancellationToken cancellationToken = default)
    {
        var account = new Account { OwnerId = tenant.UserId ?? throw new ForbiddenException("Sign in to create records.", "no_user") };
        await ApplyAsync(account, request, cancellationToken);
        account = await savePipeline.ExecuteCreateAsync("accounts", account, async () =>
        {
            db.Accounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
            return account;
        }, cancellationToken);
        await NotifyOwnerAsync(account.OwnerId, account.Id, account.Name, cancellationToken);
        return await MapAsync(account, cancellationToken);
    }

    public async Task<AccountDto> UpdateAsync(Guid id, SaveAccountRequest request, CancellationToken cancellationToken = default)
    {
        var account = await LoadScopedAsync(id, cancellationToken);
        var previousOwner = account.OwnerId;
        var previousSnapshot = new Account
        {
            Id = account.Id,
            Name = account.Name,
            Website = account.Website,
            Phone = account.Phone,
            Industry = account.Industry,
            ParentAccountId = account.ParentAccountId,
            OwnerId = account.OwnerId,
            CustomFields = account.CustomFields
        };

        await ApplyAsync(account, request, cancellationToken);
        account = await savePipeline.ExecuteUpdateAsync("accounts", account, previousSnapshot, async () =>
        {
            await db.SaveChangesAsync(cancellationToken);
            return account;
        }, cancellationToken);

        if (account.OwnerId != previousOwner)
        {
            await NotifyOwnerAsync(account.OwnerId, account.Id, account.Name, cancellationToken);
        }

        return await MapAsync(account, cancellationToken);
    }

    private Task NotifyOwnerAsync(Guid ownerId, Guid accountId, string name, CancellationToken cancellationToken)
        => notifications.NotifyAsync(new NotificationRequest([ownerId], NotificationTypes.AccountAssigned, $"Account assigned to you: {name}", null, "accounts", accountId, $"/accounts/{accountId}"), cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await LoadScopedAsync(id, cancellationToken);
        if (await db.Deals.AnyAsync(d => d.AccountId == id, cancellationToken))
        {
            throw new BusinessRuleException("account_has_deals", "This account has deals. Move or delete the deals first.");
        }

        if (await db.Accounts.AnyAsync(a => a.ParentAccountId == id, cancellationToken))
        {
            throw new BusinessRuleException("account_has_children", "This account is the parent of other accounts. Change their parent first.");
        }

        // Contacts stay; they just no longer belong to a company. Contacts the caller cannot see must not be changed behind their back.
        var linked = db.Contacts.Where(c => c.AccountId == id);
        var inScope = await RecordHelpers.InScopeAsync(linked, scope, AppModule.Contacts, c => c.OwnerId, cancellationToken);
        if (await linked.CountAsync(cancellationToken) != await inScope.CountAsync(cancellationToken))
        {
            throw new BusinessRuleException("account_has_other_contacts", "This account has contacts you cannot manage. Ask their owner or an administrator to move them first.");
        }

        await inScope.ExecuteUpdateAsync(s => s.SetProperty(c => c.AccountId, (Guid?)null), cancellationToken);
        db.Accounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> ExportCsvAsync(ListQuery query, CancellationToken cancellationToken = default)
    {
        const int pageSize = 200;
        var items = new List<AccountListItemDto>();
        for (var pageNumber = 1; items.Count < 10000; pageNumber++)
        {
            var page = await QueryAsync(query with { Page = pageNumber, PageSize = pageSize }, cancellationToken);
            items.AddRange(page.Items);
            if (page.Items.Count < pageSize || items.Count >= page.Total) break;
        }

        var builder = new StringBuilder("id,name,website,phone,industry,parent,contacts,owner,created_at\n");
        foreach (var a in items)
        {
            var parts = new[] { a.Id.ToString(), a.Name, a.Website ?? "", a.Phone ?? "", a.Industry ?? "", a.ParentName ?? "", a.ContactCount.ToString(CultureInfo.InvariantCulture), a.OwnerName ?? "", a.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture) };
            builder.AppendLine(string.Join(",", parts.Select(CsvText.Escape)));
        }

        return builder.ToString();
    }

    public Task<ImportResultDto> ImportCsvAsync(string csv, CancellationToken cancellationToken = default)
        => RecordHelpers.ImportAsync(csv, ImportAliases, "name", "Name",
            async (cell, ct) =>
            {
                var lowered = cell("name")!.ToLower();
                return await db.Accounts.AnyAsync(a => a.Name.ToLower() == lowered, ct);
            },
            async (cell, ct) => await CreateAsync(new SaveAccountRequest(cell("name")!, cell("website"), cell("phone"), cell("industry"), null, null, null, null), ct),
            cancellationToken);

    private async Task ApplyAsync(Account account, SaveAccountRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw RecordHelpers.Invalid("name", "Name is required.");
        }

        var name = request.Name.Trim();
        var lowered = name.ToLower();
        if (await db.Accounts.AnyAsync(a => a.Id != account.Id && a.Name.ToLower() == lowered, cancellationToken))
        {
            throw new ConflictException("account_exists", $"An account named {name} already exists.");
        }

        if (request.ParentAccountId is { } parentId)
        {
            if (parentId == account.Id)
            {
                throw RecordHelpers.Invalid("parentAccountId", "An account cannot be its own parent.");
            }

            if (!await (await ScopedAsync(cancellationToken)).AnyAsync(a => a.Id == parentId, cancellationToken))
            {
                throw RecordHelpers.Invalid("parentAccountId", "Choose an existing account as the parent.");
            }

            // Walk up from the chosen parent: if it reaches this account, saving would create a loop.
            var cursor = (Guid?)parentId;
            for (var depth = 0; cursor is not null && depth < 50; depth++)
            {
                var current = await db.Accounts.AsNoTracking().Where(a => a.Id == cursor).Select(a => new { a.Id, a.ParentAccountId }).FirstOrDefaultAsync(cancellationToken)
                    ?? throw RecordHelpers.Invalid("parentAccountId", "Choose an existing account as the parent.");
                if (current.ParentAccountId == account.Id)
                {
                    throw RecordHelpers.Invalid("parentAccountId", "That would make a loop: the chosen account already sits under this one.");
                }

                cursor = current.ParentAccountId;
            }
        }

        var customErrors = CustomFieldRules.ValidateValues(await fields.CustomRulesAsync("accounts", cancellationToken),
            request.CustomFields?.ToDictionary(kv => kv.Key, kv => (JsonElement?)kv.Value) ?? []);
        if (customErrors.Count > 0)
        {
            throw new ValidationException(customErrors);
        }

        account.Name = name;
        account.Website = RecordHelpers.Clean(request.Website);
        account.Phone = RecordHelpers.Clean(request.Phone);
        account.Industry = RecordHelpers.Clean(request.Industry);
        account.Description = request.Description;
        account.ParentAccountId = request.ParentAccountId;
        if (request.OwnerId is { } owner)
        {
            if (owner != account.OwnerId)
            {
                await RecordHelpers.EnsureOwnerAsync(db, scope, AppModule.Accounts, owner, cancellationToken);
            }

            account.OwnerId = owner;
        }

        if (request.CustomFields is not null)
        {
            account.CustomFields = RecordHelpers.CustomDoc(request.CustomFields);
        }
    }

    private async Task<AccountDto> MapAsync(Account a, CancellationToken cancellationToken)
    {
        var scopedAccounts = await ScopedAsync(cancellationToken);
        var parent = a.ParentAccountId is { } id ? await scopedAccounts.AsNoTracking().Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken) : null;
        return new AccountDto(a.Id, a.Name, a.Website, a.Phone, a.Industry, a.Description, a.ParentAccountId, parent, a.OwnerId, a.CreatedAt, RecordHelpers.Custom(a.CustomFields));
    }

    private async Task<IQueryable<Account>> ScopedAsync(CancellationToken cancellationToken)
    {
        var ids = await scope.GetScopeUserIdsAsync(AppModule.Accounts, cancellationToken);
        var query = db.Accounts.AsQueryable();
        return ids is null ? query : query.Where(a => ids.Contains(a.OwnerId));
    }

    private async Task<Account> LoadScopedAsync(Guid id, CancellationToken cancellationToken)
        => await (await ScopedAsync(cancellationToken)).FirstOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw new NotFoundException("Account", id);
}

/// <summary>Searches contacts, accounts and leads for the box at the top of every page.</summary>
public sealed class GlobalSearchService(AppDbContext db, IDataScopeService scope)
{
    public async Task<IReadOnlyList<SearchHitDto>> SearchAsync(string? term, CancellationToken cancellationToken = default)
    {
        var text = term?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text) || text.Length < 2)
        {
            return [];
        }

        var hits = new List<SearchHitDto>();

        var contactScope = await scope.GetScopeUserIdsAsync(AppModule.Contacts, cancellationToken);
        if (contactScope is not { Count: 0 })
        {
            var contacts = db.Contacts.AsNoTracking().AsQueryable();
            if (contactScope is not null) contacts = contacts.Where(c => contactScope.Contains(c.OwnerId));
            hits.AddRange((await contacts.Where(c => (c.FirstName + " " + c.LastName).ToLower().Contains(text) || (c.Email != null && c.Email.ToLower().Contains(text)))
                .OrderBy(c => c.FirstName).Take(5).ToListAsync(cancellationToken)).Select(c => new SearchHitDto("contact", c.Id, $"{c.FirstName} {c.LastName}".Trim(), c.Email ?? c.JobTitle)));
        }

        var accountScope = await scope.GetScopeUserIdsAsync(AppModule.Accounts, cancellationToken);
        if (accountScope is not { Count: 0 })
        {
            var accounts = db.Accounts.AsNoTracking().AsQueryable();
            if (accountScope is not null) accounts = accounts.Where(a => accountScope.Contains(a.OwnerId));
            hits.AddRange((await accounts.Where(a => a.Name.ToLower().Contains(text)).OrderBy(a => a.Name).Take(5).ToListAsync(cancellationToken))
                .Select(a => new SearchHitDto("account", a.Id, a.Name, a.Industry ?? a.Website)));
        }

        var leadScope = await scope.GetScopeUserIdsAsync(AppModule.Leads, cancellationToken);
        if (leadScope is not { Count: 0 })
        {
            var leads = db.Leads.AsNoTracking().Where(l => l.ConvertedAt == null);
            if (leadScope is not null) leads = leads.Where(l => leadScope.Contains(l.OwnerId));
            hits.AddRange((await leads.Where(l => (l.FirstName + " " + l.LastName).ToLower().Contains(text) || (l.Email != null && l.Email.ToLower().Contains(text)) || (l.CompanyName != null && l.CompanyName.ToLower().Contains(text)))
                .OrderBy(l => l.FirstName).Take(5).ToListAsync(cancellationToken)).Select(l => new SearchHitDto("lead", l.Id, $"{l.FirstName} {l.LastName}".Trim(), l.Email ?? l.CompanyName)));
        }

        return hits;
    }
}
