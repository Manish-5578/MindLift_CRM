using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Common;
using MindLift.Infrastructure.Contacts;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Settings;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using MindLift.Shared.Imports;

namespace MindLift.Infrastructure.Leads;

/// <summary>Normalisation used by duplicate checks (plan 7.3).</summary>
public static class LeadNormalizer
{
    public static string? Email(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>Keeps digits and a leading plus. Ignores spaces, dashes and brackets.</summary>
    public static string? Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 6)
        {
            return null;
        }

        return phone.Trim().StartsWith('+') ? "+" + digits : digits;
    }
}

/// <summary>Lead records: list, read, create, edit, disqualify, convert, duplicates, merge, import and export (plan 7.3).</summary>
public sealed class LeadService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    FieldSettingsService fields,
    IDataScopeService scope,
    AssignmentService assignment,
    INotificationService notifications,
    IRecordSavePipeline savePipeline,
    MindLift.Infrastructure.Common.BulkQueue bulkQueue,
    IPermissionService permissions)
{
    private const int MaxImportRows = 5000;
    private const int MaxExportRows = 10000;

    public async Task<PagedResult<LeadListItemDto>> QueryAsync(ListQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var source = await MatchingAsync(query, cancellationToken);

        var total = await source.CountAsync(cancellationToken);
        source = Sort(source, query.SortField, query.SortDir);
        var rows = await source.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var owners = await OwnerNamesAsync(rows.Select(r => r.OwnerId).Distinct(), cancellationToken);
        var items = rows.Select(l => new LeadListItemDto(
            l.Id, $"{l.FirstName} {l.LastName}".Trim(), l.Email, l.Phone, l.CompanyName, l.Status, l.Source, l.Rating,
            l.OwnerId, owners.GetValueOrDefault(l.OwnerId), l.CreatedAt, l.FirstContactedAt,
            l.ConvertedAt is not null, l.Status == "disqualified", l.FirstName, l.LastName)).ToList();

        return new PagedResult<LeadListItemDto>(items, total, page, pageSize);
    }

    /// <summary>The leads a list query matches (the user's scope, filters, search and any chosen ids), before sorting and paging.</summary>
    private async Task<IQueryable<Lead>> MatchingAsync(ListQuery query, CancellationToken cancellationToken)
    {
        var source = await ScopedLeadsAsync(cancellationToken);
        if (query.Ids is { Count: > 0 } ids)
        {
            source = source.Where(l => ids.Contains(l.Id));
        }

        // Converted leads now live in contacts and deals, so the lead list leaves them out.
        source = source.Where(l => l.ConvertedAt == null);

        if (query.Filter is not null)
        {
            var registry = await fields.RegistryAsync("leads", cancellationToken);
            source = source.Where(FilterCompiler.Compile<Lead>(query.Filter, registry, await FilterContextAsync(cancellationToken)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(l =>
                (l.FirstName + " " + l.LastName).ToLower().Contains(term)
                || (l.Email != null && l.Email.ToLower().Contains(term))
                || (l.CompanyName != null && l.CompanyName.ToLower().Contains(term))
                || (l.Phone != null && l.Phone.Contains(term)));
        }

        return source;
    }

    /// <summary>The ids of the leads a list query matches, at most <paramref name="limit"/>, for "select all" in bulk actions.</summary>
    public async Task<IReadOnlyList<Guid>> MatchingIdsAsync(ListQuery query, int limit, CancellationToken cancellationToken = default)
        => await (await MatchingAsync(query, cancellationToken)).OrderBy(l => l.Id).Select(l => l.Id).Take(limit).ToListAsync(cancellationToken);

    /// <summary>Assign, change status or add tags on many leads. Each lead is saved as if edited on its own page.</summary>
    public async Task<BulkActionResult> BulkAsync(BulkActionRequest request, CancellationToken cancellationToken = default)
    {
        Func<LeadDto, SaveLeadRequest, SaveLeadRequest> change = request.Action switch
        {
            BulkActions.Assign when request.OwnerId is { } owner => (_, save) => save with { OwnerId = owner },
            BulkActions.Assign => throw new ValidationException("ownerId", "Choose who to assign the leads to."),
            BulkActions.ChangeStatus when request.Status is "disqualified" => throw new ValidationException("status", "Disqualify leads one at a time, so each gets a reason."),
            BulkActions.ChangeStatus when !string.IsNullOrWhiteSpace(request.Status) => (_, save) => save with { Status = request.Status },
            BulkActions.ChangeStatus => throw new ValidationException("status", "Choose a status."),
            BulkActions.AddTags when request.Tags?.Any(t => !string.IsNullOrWhiteSpace(t)) == true => (lead, save) => save with { Tags = lead.Tags.Concat(request.Tags).ToList() },
            BulkActions.AddTags => throw new ValidationException("tags", "Enter at least one tag."),
            _ => throw new ValidationException("action", $"'{request.Action}' is not something that can be done to many leads at once."),
        };

        var ids = await BulkRunner.TargetsAsync(request.Ids, request.AllMatching, MatchingIdsAsync, cancellationToken);
        if (bulkQueue.ShouldQueue(ids))
        {
            return bulkQueue.Queue("leads", request, ids);
        }

        return await BulkRunner.RunAsync(db, ids, async (id, ct) =>
        {
            var lead = await GetAsync(id, ct);
            await UpdateAsync(id, change(lead, ToSave(lead)), ct);
        }, cancellationToken);
    }

    public async Task<BulkActionResult> BulkDeleteAsync(BulkDeleteRequest request, CancellationToken cancellationToken = default)
    {
        var ids = await BulkRunner.TargetsAsync(request.Ids, request.AllMatching, MatchingIdsAsync, cancellationToken);
        if (bulkQueue.ShouldQueue(ids))
        {
            return bulkQueue.QueueDelete("leads", ids);
        }

        return await BulkRunner.RunAsync(db, ids, DeleteAsync, cancellationToken);
    }

    /// <summary>The lead's current values as a save request, so a bulk change alters only the one thing it is about.</summary>
    private static SaveLeadRequest ToSave(LeadDto lead) => new(
        lead.FirstName, lead.LastName, lead.Email, lead.Phone, lead.CompanyName, lead.Status, lead.Source, lead.Rating, lead.Description,
        lead.Tags, lead.OwnerId, lead.CustomFields);

    public async Task<LeadDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var lead = await (await ScopedLeadsAsync(cancellationToken)).FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new NotFoundException("Lead", id);
        return Map(lead);
    }

    public async Task<LeadDto> CreateAsync(SaveLeadRequest request, CancellationToken cancellationToken = default)
    {
        var lead = new Lead();
        await ApplyAsync(lead, request, isNew: true, cancellationToken);

        if (request.OwnerId is null)
        {
            lead.OwnerId = await assignment.ChooseOwnerAsync(lead, cancellationToken);
            lead.AssignedAt = clock.UtcNow;
        }

        lead = await savePipeline.ExecuteCreateAsync("leads", lead, async () =>
        {
            db.Leads.Add(lead);
            await db.SaveChangesAsync(cancellationToken);
            return lead;
        }, cancellationToken);

        await NotifyAssignedAsync(lead, cancellationToken);
        return Map(lead);
    }

    public async Task<LeadDto> UpdateAsync(Guid id, SaveLeadRequest request, CancellationToken cancellationToken = default)
    {
        var lead = await LoadScopedAsync(id, cancellationToken);
        if (lead.ConvertedAt is not null)
        {
            throw new ForbiddenException("This lead has been converted and can no longer be edited. Edit the contact or deal instead.", "lead_converted");
        }

        var previousOwner = lead.OwnerId;
        var previousSnapshot = new Lead
        {
            Id = lead.Id,
            FirstName = lead.FirstName,
            LastName = lead.LastName,
            Email = lead.Email,
            Phone = lead.Phone,
            CompanyName = lead.CompanyName,
            Status = lead.Status,
            Source = lead.Source,
            Rating = lead.Rating,
            OwnerId = lead.OwnerId,
            CustomFields = lead.CustomFields
        };

        await ApplyAsync(lead, request, isNew: false, cancellationToken);
        if (lead.OwnerId != previousOwner)
        {
            lead.AssignedAt = clock.UtcNow;
        }

        lead = await savePipeline.ExecuteUpdateAsync("leads", lead, previousSnapshot, async () =>
        {
            await db.SaveChangesAsync(cancellationToken);
            return lead;
        }, cancellationToken);

        if (lead.OwnerId != previousOwner)
        {
            await NotifyAssignedAsync(lead, cancellationToken);
        }

        return Map(lead);
    }

    private Task NotifyAssignedAsync(Lead lead, CancellationToken cancellationToken)
        => notifications.NotifyAsync(new NotificationRequest(
            [lead.OwnerId], NotificationTypes.LeadAssigned, $"Lead assigned to you: {$"{lead.FirstName} {lead.LastName}".Trim()}",
            lead.CompanyName ?? lead.Email, "leads", lead.Id, $"/leads/{lead.Id}"), cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var lead = await LoadScopedAsync(id, cancellationToken);
        if (lead.ConvertedAt is not null)
        {
            throw new ForbiddenException("Converted leads cannot be deleted.", "lead_converted");
        }

        db.Leads.Remove(lead);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LeadDto> DisqualifyAsync(Guid id, DisqualifyLeadRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["reason"] = ["Choose a reason."] });
        }

        var lead = await LoadScopedAsync(id, cancellationToken);
        if (lead.ConvertedAt is not null)
        {
            throw new ForbiddenException("Converted leads cannot be disqualified.", "lead_converted");
        }

        lead.Status = "disqualified";
        lead.DisqualifyReason = request.Reason.Trim() + (string.IsNullOrWhiteSpace(request.Notes) ? string.Empty : " — " + request.Notes.Trim());
        await db.SaveChangesAsync(cancellationToken);
        return Map(lead);
    }

    /// <summary>
    /// Creates the account, contact and deal from a lead, then marks the lead converted (plan 7.3).
    /// Everything happens in one transaction. A new account with the same name as an existing one is refused,
    /// so the user chooses to link it instead.
    /// </summary>
    public async Task<ConvertLeadResult> ConvertAsync(Guid id, ConvertLeadRequest request, CancellationToken cancellationToken = default)
    {
        var lead = await LoadScopedAsync(id, cancellationToken);
        if (lead.ConvertedAt is not null)
        {
            throw new ConflictException("already_converted", "This lead has already been converted.");
        }

        if (lead.Status == "disqualified")
        {
            throw new BusinessRuleException("lead_disqualified", "A disqualified lead cannot be converted. Reopen it first.");
        }

        var hasActivities = await db.Activities.AnyAsync(a => a.LeadId == lead.Id, cancellationToken);
        await RequireAsync(AppModule.Accounts, request.AccountMode == "create" ? PermissionAction.Create : PermissionAction.Edit, request.AccountMode is "create" or "existing", cancellationToken);
        await RequireAsync(AppModule.Contacts, request.ContactMode == "existing" ? PermissionAction.Edit : PermissionAction.Create, true, cancellationToken);
        await RequireAsync(AppModule.Deals, PermissionAction.Create, request.CreateDeal, cancellationToken);
        await RequireAsync(AppModule.Activities, PermissionAction.Edit, hasActivities, cancellationToken);

        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        Account? account = null;
        if (request.AccountMode == "create")
        {
            var name = (request.AccountName ?? lead.CompanyName)?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ValidationException(new Dictionary<string, string[]> { ["accountName"] = ["Enter the company name."] });
            }

            var existing = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Name.ToLower() == name.ToLower(), cancellationToken);
            if (existing is not null)
            {
                throw new ConflictException("account_exists", $"An account named {existing.Name} already exists. Link it instead of creating a new one.");
            }

            account = new Account { Name = name, OwnerId = lead.OwnerId };
            db.Accounts.Add(account);
        }
        else if (request.AccountMode == "existing")
        {
            account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.ExistingAccountId, cancellationToken)
                ?? throw new NotFoundException("Account", request.ExistingAccountId);

            var accScope = await scope.GetScopeUserIdsAsync(AppModule.Accounts, cancellationToken);
            if (accScope is not null && !accScope.Contains(account.OwnerId))
            {
                throw new ForbiddenException("You do not have permission to link this account.", "account_forbidden");
            }
        }

        Contact contact;
        if (request.ContactMode == "existing")
        {
            contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == request.ExistingContactId, cancellationToken)
                ?? throw new NotFoundException("Contact", request.ExistingContactId);

            var conScope = await scope.GetScopeUserIdsAsync(AppModule.Contacts, cancellationToken);
            if (conScope is not null && !conScope.Contains(contact.OwnerId))
            {
                throw new ForbiddenException("You do not have permission to link this contact.", "contact_forbidden");
            }
        }
        else
        {
            contact = new Contact
            {
                FirstName = lead.FirstName,
                LastName = lead.LastName,
                Email = lead.Email,
                Phone = lead.Phone,
                NormalizedEmail = lead.NormalizedEmail,
                NormalizedPhone = lead.NormalizedPhone,
                AccountId = account?.Id,
                Type = "prospect",
                OwnerId = lead.OwnerId,
                CustomFields = await CopyCustomFieldsAsync(lead.CustomFields, "contacts", cancellationToken),
            };
            db.Contacts.Add(contact);
        }

        if (account is not null && contact.AccountId is null)
        {
            contact.AccountId = account.Id;
        }

        Deal? deal = null;
        if (request.CreateDeal)
        {
            var pipelineId = request.PipelineId ?? await db.Pipelines.Where(p => p.IsDefault).Select(p => p.Id).FirstOrDefaultAsync(cancellationToken);
            var stages = await db.PipelineStages.AsNoTracking().Where(s => s.PipelineId == pipelineId).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
            var stage = stages.FirstOrDefault(s => s.Id == request.StageId) ?? stages.FirstOrDefault(s => s.StageType == "Open")
                ?? throw new BusinessRuleException("no_pipeline", "Choose a pipeline with an open stage for the deal.");
            if (stage.StageType != "Open")
            {
                throw new BusinessRuleException("stage_not_open", "A new deal must start in an open stage.");
            }

            deal = new Deal
            {
                Name = request.DealName?.Trim() is { Length: > 0 } dealName ? dealName
                    : $"{account?.Name ?? $"{lead.FirstName} {lead.LastName}".Trim()} – {lead.Source ?? "lead"}",
                PipelineId = stage.PipelineId,
                StageId = stage.Id,
                Status = "Open",
                Amount = request.Amount ?? 0,
                Currency = request.Currency ?? settings.BaseCurrency,
                Probability = stage.Probability,
                ExpectedCloseDate = request.ExpectedCloseDate,
                AccountId = account?.Id,
                CustomFields = await CopyCustomFieldsAsync(lead.CustomFields, "deals", cancellationToken),
                OwnerId = lead.OwnerId,
                PrimaryContactId = contact.Id,
                StageChangedAt = clock.UtcNow,
            };
            db.Deals.Add(deal);
            db.DealContacts.Add(new DealContact { DealId = deal.Id, ContactId = contact.Id, IsPrimary = true });
            db.DealStageHistory.Add(new DealStageHistory { DealId = deal.Id, ToStageId = stage.Id, ChangedAt = clock.UtcNow, ChangedById = tenant.UserId });
        }

        lead.Status = "converted";
        lead.ConvertedAt = clock.UtcNow;
        lead.ConvertedById = tenant.UserId;
        lead.ConvertedAccountId = account?.Id;
        lead.ConvertedDealId = deal?.Id;
        await db.SaveChangesAsync(cancellationToken);

        // Contacts that were just created need their id, which exists before SaveChanges (client-generated).
        lead.ConvertedContactId = contact.Id;
        await db.SaveChangesAsync(cancellationToken);

        // The lead's activities move with it: they now also belong to the contact, company and deal.
        var leadActivities = await db.Activities.Where(a => a.LeadId == lead.Id).ToListAsync(cancellationToken);
        if (leadActivities.Count > 0)
        {
            await RequireAsync(AppModule.Activities, PermissionAction.Edit, true, cancellationToken);
            var activityScope = await scope.GetScopeUserIdsAsync(AppModule.Activities, cancellationToken);
            if (activityScope is not null)
            {
                var outOfScope = leadActivities.Any(a => !activityScope.Contains(a.OwnerId));
                if (outOfScope)
                {
                    throw new ForbiddenException("One or more activities linked to this lead are outside your activity edit scope.", "activity_out_of_scope");
                }
            }

            var contactId = contact.Id;
            var accountId = account?.Id;
            var dealId = deal?.Id;

            foreach (var a in leadActivities)
            {
                a.ContactId = contactId;
                a.AccountId = accountId;
                a.DealId = dealId;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return new ConvertLeadResult(contact.Id, account?.Id, deal?.Id);
    }

    public async Task<IReadOnlyList<DuplicateCandidateDto>> CheckDuplicatesAsync(CheckDuplicatesRequest request, CancellationToken cancellationToken = default)
    {
        var email = LeadNormalizer.Email(request.Email);
        var phone = LeadNormalizer.Phone(request.Phone);
        var name = $"{request.FirstName} {request.LastName}".Trim().ToLowerInvariant();
        var company = request.CompanyName?.Trim().ToLowerInvariant();
        var results = new List<DuplicateCandidateDto>();

        var leads = await (await ScopedLeadsAsync(cancellationToken))
            .Where(l => l.ConvertedAt == null)
            .Where(l => (email != null && l.NormalizedEmail == email) || (phone != null && l.NormalizedPhone == phone))
            .ToListAsync(cancellationToken);
        results.AddRange(leads.Select(l => new DuplicateCandidateDto(l.Id, "lead", $"{l.FirstName} {l.LastName}".Trim(), l.Email, l.Phone,
            email != null && l.NormalizedEmail == email ? "Same email" : "Same phone")));

        var contactScope = await scope.GetScopeUserIdsAsync(AppModule.Contacts, cancellationToken);
        var contactQuery = db.Contacts.AsNoTracking().AsQueryable();
        if (contactScope is not null)
        {
            contactQuery = contactQuery.Where(c => contactScope.Contains(c.OwnerId));
        }

        var contacts = await contactQuery
            .Where(c => (email != null && c.NormalizedEmail == email) || (phone != null && c.NormalizedPhone == phone))
            .ToListAsync(cancellationToken);
        results.AddRange(contacts.Select(c => new DuplicateCandidateDto(c.Id, "contact", $"{c.FirstName} {c.LastName}".Trim(), c.Email, c.Phone,
            email != null && c.NormalizedEmail == email ? "Same email" : "Same phone")));

        // Similar name at the same company (exact, case-insensitive; fuzzy matching comes with pg_trgm in M15).
        if (company is not null)
        {
            var sameCompany = await (await ScopedLeadsAsync(cancellationToken))
                .Where(l => l.ConvertedAt == null && l.CompanyName != null && l.CompanyName.ToLower() == company)
                .ToListAsync(cancellationToken);
            results.AddRange(sameCompany.Where(l => !results.Any(r => r.Id == l.Id) && $"{l.FirstName} {l.LastName}".Trim().ToLowerInvariant() == name)
                .Select(l => new DuplicateCandidateDto(l.Id, "lead", $"{l.FirstName} {l.LastName}".Trim(), l.Email, l.Phone, "Same name at the same company")));
        }

        return results;
    }

    /// <summary>Moves the useful values of the duplicates into the master and removes the duplicates (soft delete).</summary>
    public async Task<LeadDto> MergeAsync(Guid masterId, IReadOnlyList<Guid> duplicateIds, CancellationToken cancellationToken = default)
    {
        // Merging removes the duplicates, so it needs the right to delete leads, and every duplicate must be in scope.
        await RequireAsync(AppModule.Leads, PermissionAction.Delete, true, cancellationToken);

        if (duplicateIds == null || duplicateIds.Count == 0)
        {
            throw new ValidationException("duplicateIds", "At least one duplicate lead ID must be provided.");
        }

        if (duplicateIds.Contains(masterId))
        {
            throw new ValidationException("duplicateIds", "Master lead cannot be listed as a duplicate of itself.");
        }

        if (duplicateIds.Distinct().Count() != duplicateIds.Count)
        {
            throw new ValidationException("duplicateIds", "Duplicate lead IDs must be unique.");
        }

        var master = await LoadScopedAsync(masterId, cancellationToken);
        if (master.ConvertedAt is not null)
        {
            throw new ForbiddenException("Converted leads cannot be merged.", "lead_converted");
        }

        var requested = duplicateIds.ToList();
        var duplicates = await (await ScopedLeadsAsync(cancellationToken))
            .Where(l => requested.Contains(l.Id) && l.ConvertedAt == null)
            .ToListAsync(cancellationToken);

        if (duplicates.Count != requested.Count)
        {
            throw new NotFoundException("Lead", requested.First(r => duplicates.All(d => d.Id != r)));
        }

        // Validate each duplicate against server-side duplicate-matching rules before merge
        foreach (var duplicate in duplicates)
        {
            var isDuplicate = (!string.IsNullOrWhiteSpace(master.NormalizedEmail) && master.NormalizedEmail == duplicate.NormalizedEmail)
                || (!string.IsNullOrWhiteSpace(master.NormalizedPhone) && master.NormalizedPhone == duplicate.NormalizedPhone)
                || (!string.IsNullOrWhiteSpace(master.CompanyName) && string.Equals(master.CompanyName.Trim(), duplicate.CompanyName?.Trim(), StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(master.FirstName) && !string.IsNullOrWhiteSpace(master.LastName)
                    && string.Equals(master.FirstName, duplicate.FirstName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(master.LastName, duplicate.LastName, StringComparison.OrdinalIgnoreCase));

            if (!isDuplicate)
            {
                throw new ValidationException("duplicateIds", $"Lead '{duplicate.FirstName} {duplicate.LastName}' (ID: {duplicate.Id}) is not a duplicate of the master lead.");
            }
        }

        foreach (var duplicate in duplicates)
        {
            master.Email ??= duplicate.Email;
            master.NormalizedEmail ??= duplicate.NormalizedEmail;
            master.Phone ??= duplicate.Phone;
            master.NormalizedPhone ??= duplicate.NormalizedPhone;
            master.CompanyName ??= duplicate.CompanyName;
            master.Description ??= duplicate.Description;
            master.Tags = master.Tags.Union(duplicate.Tags).ToArray();
            db.Leads.Remove(duplicate);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Map(master);
    }

    /// <summary>
    /// CSV import (plan 4.10). The header row is matched to field keys or labels, ignoring case, spaces and underscores.
    /// Rows whose email or phone already exists are skipped. Returns counts and the first errors.
    /// </summary>
    public async Task<ImportResultDto> ImportCsvAsync(string csv, CancellationToken cancellationToken = default)
    {
        var rows = CsvText.Parse(csv);
        if (rows.Count < 2)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["The file needs a header row and at least one row."] });
        }

        if (rows.Count - 1 > MaxImportRows)
        {
            throw new BusinessRuleException("import_too_large", $"Import at most {MaxImportRows} rows at a time.");
        }

        // The same rules the import wizard uses to show people how their columns will be read.
        var columns = LeadImportColumns.Map(rows[0]);

        if (columns[LeadImportColumns.RequiredField] < 0)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["The file needs a 'First name' column."] });
        }

        var created = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            string? Cell(string key) => columns[key] >= 0 && columns[key] < row.Count ? row[columns[key]].Trim() : null;

            var first = Cell("first_name");
            if (string.IsNullOrWhiteSpace(first))
            {
                failed++;
                if (errors.Count < 20) errors.Add($"Row {i + 1}: first name is missing.");
                continue;
            }

            var email = LeadNormalizer.Email(Cell("email"));
            var phone = LeadNormalizer.Phone(Cell("phone"));
            if ((email is not null && await db.Leads.AnyAsync(l => l.NormalizedEmail == email && l.ConvertedAt == null, cancellationToken))
                || (phone is not null && await db.Leads.AnyAsync(l => l.NormalizedPhone == phone && l.ConvertedAt == null, cancellationToken)))
            {
                skipped++;
                continue;
            }

            var source = Cell("source");
            var request = new SaveLeadRequest(
                first, Cell("last_name") ?? string.Empty, Cell("email"), Cell("phone"), Cell("company_name"),
                "new", string.IsNullOrWhiteSpace(source) ? "import" : source.ToLowerInvariant().Replace(' ', '_'),
                null, Cell("description"), null, null, null);

            try
            {
                await CreateAsync(request, cancellationToken);
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

    /// <summary>CSV export of the current list (plan 4.10). Respects scope and filters; capped at 10,000 rows.</summary>
    public async Task<string> ExportCsvAsync(ListQuery query, CancellationToken cancellationToken = default)
    {
        // QueryAsync never returns more than 200 rows a page, so walk the pages until everything (up to the export limit) is read.
        const int pageSize = 200;
        var items = new List<LeadListItemDto>();
        for (var pageNumber = 1; items.Count < MaxExportRows; pageNumber++)
        {
            var page = await QueryAsync(query with { Page = pageNumber, PageSize = pageSize }, cancellationToken);
            items.AddRange(page.Items);
            if (page.Items.Count < pageSize || items.Count >= page.Total)
            {
                break;
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("id,first_name,last_name,email,phone,company,status,source,rating,owner,created_at");
        foreach (var item in items.Take(MaxExportRows))
        {
            var parts = new[]
            {
                item.Id.ToString(), item.FirstName, item.LastName, item.Email ?? string.Empty, item.Phone ?? string.Empty, item.CompanyName ?? string.Empty,
                item.Status, item.Source ?? string.Empty, item.Rating ?? string.Empty, item.OwnerName ?? string.Empty,
                item.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            };
            builder.AppendLine(string.Join(",", parts.Select(CsvText.Escape)));
        }

        return builder.ToString();
    }

    // ---------- helpers ----------

    private async Task ApplyAsync(Lead lead, SaveLeadRequest request, bool isNew, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["firstName"] = ["First name is required."] });
        }

        var status = request.Status ?? (isNew ? "new" : lead.Status);
        if (status == "disqualified" || status == "converted")
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["status"] = [status == "converted" ? "Use Convert to change a lead to converted." : "Use Disqualify to disqualify a lead."],
            });
        }

        if (!await db.PicklistValues.AnyAsync(p => p.PicklistKey == "lead_status" && p.Value == status && p.IsActive, cancellationToken))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["status"] = ["Choose a valid status."] });
        }

        var customValues = request.CustomFields?.ToDictionary(kv => kv.Key, kv => (JsonElement?)kv.Value) ?? [];
        var customErrors = CustomFieldRules.ValidateValues(await fields.CustomRulesAsync("leads", cancellationToken), customValues);
        if (customErrors.Count > 0)
        {
            throw new ValidationException(customErrors);
        }

        lead.FirstName = request.FirstName.Trim();
        lead.LastName = request.LastName.Trim();
        lead.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        lead.NormalizedEmail = LeadNormalizer.Email(request.Email);
        lead.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        lead.NormalizedPhone = LeadNormalizer.Phone(request.Phone);
        lead.CompanyName = string.IsNullOrWhiteSpace(request.CompanyName) ? null : request.CompanyName.Trim();
        lead.Status = status;
        lead.Source = request.Source ?? lead.Source;
        lead.Rating = request.Rating ?? lead.Rating;
        lead.Description = request.Description;
        lead.Tags = request.Tags?.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToArray() ?? lead.Tags;
        if (request.OwnerId is { } owner)
        {
            if (owner != lead.OwnerId)
            {
                await RecordHelpers.EnsureOwnerAsync(db, scope, AppModule.Leads, owner, cancellationToken);
            }

            lead.OwnerId = owner;
        }

        if (request.CustomFields is not null)
        {
            lead.CustomFields = JsonDocument.Parse(JsonSerializer.Serialize(request.CustomFields));
        }
    }

    private async Task RequireAsync(AppModule module, PermissionAction action, bool when, CancellationToken cancellationToken)
    {
        if (when && !await permissions.HasAsync(module, action, cancellationToken))
        {
            throw new ForbiddenException($"You need {action} permission on {module} for this.", "forbidden");
        }
    }

    private async Task<IQueryable<Lead>> ScopedLeadsAsync(CancellationToken cancellationToken)
    {
        var ids = await scope.GetScopeUserIdsAsync(AppModule.Leads, cancellationToken);
        var query = db.Leads.AsQueryable();
        return ids is null ? query : query.Where(l => ids.Contains(l.OwnerId));
    }

    private async Task<Lead> LoadScopedAsync(Guid id, CancellationToken cancellationToken)
        => await (await ScopedLeadsAsync(cancellationToken)).FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
           ?? throw new NotFoundException("Lead", id);

    private async Task<FilterContext> FilterContextAsync(CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);
        return new FilterContext(
            TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId),
            clock.UtcNow,
            (DayOfWeek)settings.WeekStartDay,
            settings.FiscalYearStartMonth,
            tenant.UserId);
    }

    private static IQueryable<Lead> Sort(IQueryable<Lead> query, string? field, string? dir)
    {
        var descending = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
        Expression<Func<Lead, object>> key = field switch
        {
            "first_name" => l => l.FirstName,
            "last_name" => l => l.LastName,
            "status" => l => l.Status,
            "source" => l => l.Source!,
            "company_name" => l => l.CompanyName!,
            _ => l => l.CreatedAt,
        };

        return descending ? query.OrderByDescending(key).ThenBy(l => l.Id) : query.OrderBy(key).ThenBy(l => l.Id);
    }

    private async Task<Dictionary<Guid, string>> OwnerNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.ToList();
        return await db.Users.AsNoTracking()
            .Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);
    }

    /// <summary>Copies custom values from a lead to a contact or deal when both define the same key (plan 7.3, step 3).</summary>
    private async Task<JsonDocument?> CopyCustomFieldsAsync(JsonDocument? source, string entityKey, CancellationToken cancellationToken)
    {
        if (source is null)
        {
            return null;
        }

        var targetKeys = (await fields.CustomRulesAsync(entityKey, cancellationToken)).Select(r => r.Key).ToHashSet();
        var copied = new Dictionary<string, JsonElement>();
        foreach (var property in source.RootElement.EnumerateObject())
        {
            if (targetKeys.Contains(property.Name))
            {
                copied[property.Name] = property.Value.Clone();
            }
        }

        return copied.Count == 0 ? null : JsonDocument.Parse(JsonSerializer.Serialize(copied));
    }

    private static LeadDto Map(Lead l)
    {
        var custom = new Dictionary<string, JsonElement>();
        if (l.CustomFields is not null)
        {
            foreach (var property in l.CustomFields.RootElement.EnumerateObject())
            {
                custom[property.Name] = property.Value.Clone();
            }
        }

        return new LeadDto(
            l.Id, l.FirstName, l.LastName, l.Email, l.Phone, l.CompanyName, l.Status, l.Source, l.Rating, l.Description,
            l.Tags, l.OwnerId, l.DisqualifyReason, l.CreatedAt, l.FirstContactedAt, l.ConvertedAt,
            l.ConvertedContactId, l.ConvertedAccountId, l.ConvertedDealId, custom);
    }
}

/// <summary>Assigns an owner to a new lead (plan 7.3, Assignment).</summary>
public sealed class AssignmentService(AppDbContext db, IClock clock)
{
    public async Task<Guid> ChooseOwnerAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        var rules = await db.AssignmentRules.Where(r => r.IsActive).OrderBy(r => r.Priority).ToListAsync(cancellationToken);
        foreach (var rule in rules)
        {
            if (!Matches(rule, lead))
            {
                continue;
            }

            var userIds = JsonSerializer.Deserialize<List<Guid>>(rule.UserIds.RootElement.GetRawText()) ?? [];
            var active = await db.Users.AsNoTracking().Where(u => u.IsActive && userIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken);
            var ordered = userIds.Where(active.Contains).ToList();
            if (ordered.Count == 0)
            {
                continue;
            }

            switch (rule.Method)
            {
                case "SpecificUser":
                    return ordered[0];

                case "LoadBalanced":
                {
                    var loads = await db.Leads.AsNoTracking()
                        .Where(l => ordered.Contains(l.OwnerId) && l.ConvertedAt == null && l.Status != "disqualified")
                        .GroupBy(l => l.OwnerId)
                        .Select(g => new { g.Key, Count = g.Count() })
                        .ToListAsync(cancellationToken);
                    return ordered.OrderBy(id => loads.FirstOrDefault(x => x.Key == id)?.Count ?? 0).ThenBy(id => ordered.IndexOf(id)).First();
                }

                default:
                {
                    var next = (rule.LastAssignedIndex + 1) % ordered.Count;
                    rule.LastAssignedIndex = next;
                    return ordered[next];
                }
            }
        }

        // No rule matched: the workspace owner gets the lead.
        return await db.Users.AsNoTracking().Where(u => u.IsTenantOwner).Select(u => u.Id).FirstAsync(cancellationToken);
    }

    /// <summary>Evaluates the rule's conditions on the lead in memory. Custom-field conditions cannot be evaluated here and do not match.</summary>
    private bool Matches(AssignmentRule rule, Lead lead)
    {
        if (rule.Conditions is null)
        {
            return true;
        }

        var group = JsonSerializer.Deserialize<FilterGroup>(rule.Conditions.RootElement.GetRawText()) ?? new FilterGroup();
        try
        {
            var registry = SystemFields.Get("leads").SystemFields;
            var context = new FilterContext(TimeZoneInfo.Utc, clock.UtcNow, DayOfWeek.Monday, 1, null);
            var predicate = FilterCompiler.Compile<Lead>(group, registry, context).Compile();
            return predicate(lead);
        }
        catch (Exception ex) when (ex is ValidationException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>Assignment rules and web forms: settings CRUD (plan 7.3, 7.2).</summary>
public sealed class LeadSettingsService(AppDbContext db, ITenantContext tenant, SettingsVersion version)
{
    public async Task<IReadOnlyList<AssignmentRuleDto>> ListRulesAsync(CancellationToken cancellationToken = default)
        => (await db.AssignmentRules.AsNoTracking().OrderBy(r => r.Priority).ToListAsync(cancellationToken)).Select(MapRule).ToList();

    public async Task<AssignmentRuleDto> SaveRuleAsync(Guid? id, SaveAssignmentRuleRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Method is not ("RoundRobin" or "LoadBalanced" or "SpecificUser"))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["method"] = ["Choose RoundRobin, LoadBalanced or SpecificUser."] });
        }

        if (request.UserIds.Count == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["userIds"] = ["Choose at least one user."] });
        }

        var knownUsers = await db.Users.AsNoTracking().Where(u => request.UserIds.Contains(u.Id)).CountAsync(cancellationToken);
        if (knownUsers != request.UserIds.Distinct().Count())
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["userIds"] = ["One or more users do not exist."] });
        }

        var rule = id is null ? new AssignmentRule() : await db.AssignmentRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Assignment rule", id);
        if (id is null)
        {
            db.AssignmentRules.Add(rule);
        }

        rule.Name = request.Name.Trim();
        rule.IsActive = request.IsActive;
        rule.Priority = request.Priority;
        rule.Method = request.Method;
        rule.UserIds = JsonDocument.Parse(JsonSerializer.Serialize(request.UserIds));
        rule.Conditions = request.Conditions is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(request.Conditions));
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return MapRule(rule);
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await db.AssignmentRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException("Assignment rule", id);
        db.AssignmentRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WebFormDto>> ListFormsAsync(CancellationToken cancellationToken = default)
        => (await db.WebForms.AsNoTracking().OrderBy(f => f.Name).ToListAsync(cancellationToken)).Select(MapForm).ToList();

    public async Task<WebFormDto> SaveFormAsync(Guid? id, SaveWebFormRequest request, CancellationToken cancellationToken = default)
    {
        var allowed = new[] { "first_name", "last_name", "email", "phone", "company_name", "description" };
        if (request.Fields.Any(f => !allowed.Contains(f)) || !request.Fields.Contains("first_name"))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["fields"] = ["Forms need first_name and may only use: " + string.Join(", ", allowed)] });
        }

        var form = id is null ? new WebForm { PublicToken = NewToken() } : await db.WebForms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
            ?? throw new NotFoundException("Web form", id);
        if (id is null)
        {
            db.WebForms.Add(form);
        }

        form.Name = request.Name.Trim();
        form.DefaultSource = request.DefaultSource;
        form.SuccessMessage = request.SuccessMessage;
        form.IsActive = request.IsActive;
        form.Fields = JsonDocument.Parse(JsonSerializer.Serialize(request.Fields));
        await db.SaveChangesAsync(cancellationToken);
        return MapForm(form);
    }

    public static string NewToken()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18))
            .TrimEnd('=').Replace('+', 'a').Replace('/', 'b');

    private static AssignmentRuleDto MapRule(AssignmentRule r) => new(
        r.Id, r.Name, r.IsActive, r.Priority, r.Method,
        JsonSerializer.Deserialize<List<Guid>>(r.UserIds.RootElement.GetRawText()) ?? [],
        r.Conditions is null ? null : JsonSerializer.Deserialize<FilterGroup>(r.Conditions.RootElement.GetRawText()));

    private static WebFormDto MapForm(WebForm f) => new(
        f.Id, f.Name, f.PublicToken, f.DefaultSource, f.SuccessMessage, f.IsActive, f.SubmissionCount,
        JsonSerializer.Deserialize<List<string>>(f.Fields.RootElement.GetRawText()) ?? []);
}

/// <summary>Public web form submissions. Anonymous: the form token selects the workspace (plan 7.3).</summary>
public sealed class PublicFormService(AppDbContext db, ITenantContext tenant, LeadService leads, IClock clock)
{
    public async Task<PublicFormResult> SubmitAsync(string token, PublicFormSubmission submission, CancellationToken cancellationToken = default)
    {
        var form = await db.WebForms.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.PublicToken == token && !f.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Form", token);
        if (!form.IsActive)
        {
            throw new NotFoundException("Form", token);
        }

        // A workspace that has lapsed takes no new leads, so the form acts as if it were switched off.
        var workspace = await db.Tenants.AsNoTracking().Where(t => t.Id == form.TenantId).Select(t => new { t.Status, t.TrialEndsAt }).FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || MindLift.Infrastructure.Tenancy.TenantAccess.IsLapsed(workspace.Status, workspace.TrialEndsAt, clock.UtcNow))
        {
            throw new NotFoundException("Form", token);
        }

        // Honeypot: bots fill the hidden field. Pretend success and store nothing.
        if (!string.IsNullOrEmpty(submission.Hp))
        {
            return new PublicFormResult(form.SuccessMessage, false);
        }

        using var scope = tenant.Use(form.TenantId);
        var allowed = JsonSerializer.Deserialize<List<string>>(form.Fields.RootElement.GetRawText()) ?? [];

        var firstName = !string.IsNullOrWhiteSpace(submission.FirstName) ? submission.FirstName.Trim() : "Web";
        var lastName = !string.IsNullOrWhiteSpace(submission.LastName) ? submission.LastName.Trim() : "Lead";

        var request = new SaveLeadRequest(
            firstName,
            lastName,
            allowed.Contains("email") ? submission.Email?.Trim() : null,
            allowed.Contains("phone") ? submission.Phone?.Trim() : null,
            allowed.Contains("company_name") ? submission.CompanyName?.Trim() : null,
            "new", form.DefaultSource, null,
            allowed.Contains("description") ? submission.Message?.Trim() : null,
            null, null, null);

        var created = await leads.CreateAsync(request, cancellationToken);
        var lead = await db.Leads.FirstAsync(l => l.Id == created.Id, cancellationToken);
        lead.WebFormId = form.Id;
        form.SubmissionCount++;
        await db.SaveChangesAsync(cancellationToken);
        return new PublicFormResult(form.SuccessMessage, false);
    }
}
