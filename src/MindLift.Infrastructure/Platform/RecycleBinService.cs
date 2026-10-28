using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Platform;

/// <summary>
/// Phase 2 / #8: Recycle Bin (recycle-bin).
/// Provides listing, restoring, and purging of soft-deleted records within a 60-day retention window.
/// </summary>
public sealed class RecycleBinService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    ITimelineEventBus timeline) : IRecycleBinService
{
    private const int RetentionDays = 60;

    private sealed record RawDeletedItem(
        Guid Id,
        string EntityType,
        string DisplayName,
        DateTime DeletedAt,
        Guid? UpdatedById);

    public async Task<RecycleBinPagedResult> ListAsync(RecycleBinQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No tenant is set for this operation.");
        var now = clock.UtcNow;
        var cutoff = now.AddDays(-RetentionDays);
        var targetType = query.EntityType?.Trim().ToLowerInvariant();
        var search = query.Search?.Trim().ToLowerInvariant();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        List<RawDeletedItem> allItems = [];
        int totalCount = 0;

        if (!string.IsNullOrEmpty(targetType) && targetType != "all")
        {
            (allItems, totalCount) = await QuerySpecificEntityAsync(targetType, tenantId, cutoff, search, page, pageSize, cancellationToken);
        }
        else
        {
            allItems = await QueryAllEntitiesAsync(tenantId, cutoff, search, cancellationToken);
            totalCount = allItems.Count;
            allItems = allItems
                .OrderByDescending(x => x.DeletedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        // Resolve user names for the paged items
        var userIds = allItems.Where(x => x.UpdatedById.HasValue).Select(x => x.UpdatedById!.Value).Distinct().ToList();
        var userMap = userIds.Count > 0
            ? await db.Set<AppUser>().IgnoreQueryFilters()
                .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken)
            : new Dictionary<Guid, string>();

        var items = allItems.Select(x =>
        {
            var daysRemaining = Math.Max(0, RetentionDays - (int)(now - x.DeletedAt).TotalDays);
            string? userName = null;
            if (x.UpdatedById.HasValue && userMap.TryGetValue(x.UpdatedById.Value, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                userName = name;
            }

            return new RecycleBinItemDto(
                x.Id,
                x.EntityType,
                x.DisplayName,
                x.DeletedAt,
                x.UpdatedById,
                userName,
                daysRemaining);
        }).ToList();

        return new RecycleBinPagedResult(items, totalCount, page, pageSize);
    }

    private async Task<(List<RawDeletedItem> Items, int TotalCount)> QuerySpecificEntityAsync(
        string entityType,
        Guid tenantId,
        DateTime cutoff,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        switch (entityType)
        {
            case "leads":
            {
                var q = db.Leads.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
                if (!string.IsNullOrEmpty(search))
                {
                    q = q.Where(x => x.FirstName.ToLower().Contains(search)
                                  || x.LastName.ToLower().Contains(search)
                                  || (x.CompanyName != null && x.CompanyName.ToLower().Contains(search)));
                }

                var total = await q.CountAsync(cancellationToken);
                var rows = await q.OrderByDescending(x => x.DeletedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new { x.Id, x.FirstName, x.LastName, x.CompanyName, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
                    .ToListAsync(cancellationToken);

                var items = rows.Select(x =>
                {
                    var name = $"{x.FirstName} {x.LastName}".Trim();
                    if (string.IsNullOrEmpty(name)) name = x.CompanyName ?? "Lead";
                    return new RawDeletedItem(x.Id, "leads", name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById);
                }).ToList();

                return (items, total);
            }
            case "contacts":
            {
                var q = db.Contacts.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
                if (!string.IsNullOrEmpty(search))
                {
                    q = q.Where(x => x.FirstName.ToLower().Contains(search)
                                  || x.LastName.ToLower().Contains(search)
                                  || (x.Email != null && x.Email.ToLower().Contains(search)));
                }

                var total = await q.CountAsync(cancellationToken);
                var rows = await q.OrderByDescending(x => x.DeletedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new { x.Id, x.FirstName, x.LastName, x.Email, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
                    .ToListAsync(cancellationToken);

                var items = rows.Select(x =>
                {
                    var name = $"{x.FirstName} {x.LastName}".Trim();
                    if (string.IsNullOrEmpty(name)) name = x.Email ?? "Contact";
                    return new RawDeletedItem(x.Id, "contacts", name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById);
                }).ToList();

                return (items, total);
            }
            case "accounts":
            {
                var q = db.Accounts.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
                if (!string.IsNullOrEmpty(search))
                {
                    q = q.Where(x => x.Name.ToLower().Contains(search));
                }

                var total = await q.CountAsync(cancellationToken);
                var rows = await q.OrderByDescending(x => x.DeletedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new { x.Id, x.Name, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
                    .ToListAsync(cancellationToken);

                var items = rows.Select(x => new RawDeletedItem(x.Id, "accounts", x.Name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById)).ToList();
                return (items, total);
            }
            case "deals":
            {
                var q = db.Deals.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
                if (!string.IsNullOrEmpty(search))
                {
                    q = q.Where(x => x.Name.ToLower().Contains(search));
                }

                var total = await q.CountAsync(cancellationToken);
                var rows = await q.OrderByDescending(x => x.DeletedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new { x.Id, x.Name, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
                    .ToListAsync(cancellationToken);

                var items = rows.Select(x => new RawDeletedItem(x.Id, "deals", x.Name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById)).ToList();
                return (items, total);
            }
            case "activities":
            {
                var q = db.Activities.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
                if (!string.IsNullOrEmpty(search))
                {
                    q = q.Where(x => x.Subject.ToLower().Contains(search));
                }

                var total = await q.CountAsync(cancellationToken);
                var rows = await q.OrderByDescending(x => x.DeletedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new { x.Id, x.Subject, x.Type, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
                    .ToListAsync(cancellationToken);

                var items = rows.Select(x =>
                {
                    var name = !string.IsNullOrWhiteSpace(x.Subject) ? $"{x.Type}: {x.Subject}" : x.Type;
                    return new RawDeletedItem(x.Id, "activities", name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById);
                }).ToList();

                return (items, total);
            }
            case "quotes":
            {
                var q = db.Quotes.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
                if (!string.IsNullOrEmpty(search))
                {
                    q = q.Where(x => x.QuoteNumber.ToLower().Contains(search) || (x.Title != null && x.Title.ToLower().Contains(search)));
                }

                var total = await q.CountAsync(cancellationToken);
                var rows = await q.OrderByDescending(x => x.DeletedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new { x.Id, x.QuoteNumber, x.Title, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
                    .ToListAsync(cancellationToken);

                var items = rows.Select(x =>
                {
                    var name = !string.IsNullOrWhiteSpace(x.Title) ? $"{x.QuoteNumber} - {x.Title}" : x.QuoteNumber;
                    return new RawDeletedItem(x.Id, "quotes", name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById);
                }).ToList();

                return (items, total);
            }
            case "invoices":
            {
                var q = db.Invoices.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
                if (!string.IsNullOrEmpty(search))
                {
                    q = q.Where(x => x.InvoiceNumber.ToLower().Contains(search));
                }

                var total = await q.CountAsync(cancellationToken);
                var rows = await q.OrderByDescending(x => x.DeletedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new { x.Id, x.InvoiceNumber, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
                    .ToListAsync(cancellationToken);

                var items = rows.Select(x => new RawDeletedItem(x.Id, "invoices", x.InvoiceNumber, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById)).ToList();
                return (items, total);
            }
            case "products":
            {
                var q = db.Products.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
                if (!string.IsNullOrEmpty(search))
                {
                    q = q.Where(x => x.Name.ToLower().Contains(search) || (x.Sku != null && x.Sku.ToLower().Contains(search)));
                }

                var total = await q.CountAsync(cancellationToken);
                var rows = await q.OrderByDescending(x => x.DeletedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new { x.Id, x.Name, x.Sku, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
                    .ToListAsync(cancellationToken);

                var items = rows.Select(x => new RawDeletedItem(x.Id, "products", x.Name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById)).ToList();
                return (items, total);
            }
            case "tickets":
            {
                var q = db.Tickets.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
                if (!string.IsNullOrEmpty(search))
                {
                    q = q.Where(x => x.Number.ToLower().Contains(search) || (x.Subject != null && x.Subject.ToLower().Contains(search)));
                }

                var total = await q.CountAsync(cancellationToken);
                var rows = await q.OrderByDescending(x => x.DeletedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new { x.Id, x.Number, x.Subject, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
                    .ToListAsync(cancellationToken);

                var items = rows.Select(x =>
                {
                    var name = !string.IsNullOrWhiteSpace(x.Subject) ? $"{x.Number}: {x.Subject}" : x.Number;
                    return new RawDeletedItem(x.Id, "tickets", name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById);
                }).ToList();

                return (items, total);
            }
            default:
                throw new ValidationException($"Unsupported entity type: '{entityType}'. Supported types: leads, contacts, accounts, deals, activities, quotes, invoices, products, tickets.");
        }
    }

    private async Task<List<RawDeletedItem>> QueryAllEntitiesAsync(
        Guid tenantId,
        DateTime cutoff,
        string? search,
        CancellationToken cancellationToken)
    {
        var items = new List<RawDeletedItem>();

        // Leads
        var leadsQuery = db.Leads.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
        if (!string.IsNullOrEmpty(search))
        {
            leadsQuery = leadsQuery.Where(x => x.FirstName.ToLower().Contains(search) || x.LastName.ToLower().Contains(search) || (x.CompanyName != null && x.CompanyName.ToLower().Contains(search)));
        }
        var leads = await leadsQuery.OrderByDescending(x => x.DeletedAt).Take(200)
            .Select(x => new { x.Id, x.FirstName, x.LastName, x.CompanyName, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
            .ToListAsync(cancellationToken);
        items.AddRange(leads.Select(x =>
        {
            var name = $"{x.FirstName} {x.LastName}".Trim();
            if (string.IsNullOrEmpty(name)) name = x.CompanyName ?? "Lead";
            return new RawDeletedItem(x.Id, "leads", name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById);
        }));

        // Contacts
        var contactsQuery = db.Contacts.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
        if (!string.IsNullOrEmpty(search))
        {
            contactsQuery = contactsQuery.Where(x => x.FirstName.ToLower().Contains(search) || x.LastName.ToLower().Contains(search) || (x.Email != null && x.Email.ToLower().Contains(search)));
        }
        var contacts = await contactsQuery.OrderByDescending(x => x.DeletedAt).Take(200)
            .Select(x => new { x.Id, x.FirstName, x.LastName, x.Email, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
            .ToListAsync(cancellationToken);
        items.AddRange(contacts.Select(x =>
        {
            var name = $"{x.FirstName} {x.LastName}".Trim();
            if (string.IsNullOrEmpty(name)) name = x.Email ?? "Contact";
            return new RawDeletedItem(x.Id, "contacts", name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById);
        }));

        // Accounts
        var accountsQuery = db.Accounts.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
        if (!string.IsNullOrEmpty(search))
        {
            accountsQuery = accountsQuery.Where(x => x.Name.ToLower().Contains(search));
        }
        var accounts = await accountsQuery.OrderByDescending(x => x.DeletedAt).Take(200)
            .Select(x => new { x.Id, x.Name, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
            .ToListAsync(cancellationToken);
        items.AddRange(accounts.Select(x => new RawDeletedItem(x.Id, "accounts", x.Name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById)));

        // Deals
        var dealsQuery = db.Deals.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
        if (!string.IsNullOrEmpty(search))
        {
            dealsQuery = dealsQuery.Where(x => x.Name.ToLower().Contains(search));
        }
        var deals = await dealsQuery.OrderByDescending(x => x.DeletedAt).Take(200)
            .Select(x => new { x.Id, x.Name, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
            .ToListAsync(cancellationToken);
        items.AddRange(deals.Select(x => new RawDeletedItem(x.Id, "deals", x.Name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById)));

        // Activities
        var activitiesQuery = db.Activities.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
        if (!string.IsNullOrEmpty(search))
        {
            activitiesQuery = activitiesQuery.Where(x => x.Subject.ToLower().Contains(search));
        }
        var activities = await activitiesQuery.OrderByDescending(x => x.DeletedAt).Take(200)
            .Select(x => new { x.Id, x.Subject, x.Type, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
            .ToListAsync(cancellationToken);
        items.AddRange(activities.Select(x =>
        {
            var name = !string.IsNullOrWhiteSpace(x.Subject) ? $"{x.Type}: {x.Subject}" : x.Type;
            return new RawDeletedItem(x.Id, "activities", name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById);
        }));

        // Quotes
        var quotesQuery = db.Quotes.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
        if (!string.IsNullOrEmpty(search))
        {
            quotesQuery = quotesQuery.Where(x => x.QuoteNumber.ToLower().Contains(search) || (x.Title != null && x.Title.ToLower().Contains(search)));
        }
        var quotes = await quotesQuery.OrderByDescending(x => x.DeletedAt).Take(200)
            .Select(x => new { x.Id, x.QuoteNumber, x.Title, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
            .ToListAsync(cancellationToken);
        items.AddRange(quotes.Select(x =>
        {
            var name = !string.IsNullOrWhiteSpace(x.Title) ? $"{x.QuoteNumber} - {x.Title}" : x.QuoteNumber;
            return new RawDeletedItem(x.Id, "quotes", name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById);
        }));

        // Invoices
        var invoicesQuery = db.Invoices.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
        if (!string.IsNullOrEmpty(search))
        {
            invoicesQuery = invoicesQuery.Where(x => x.InvoiceNumber.ToLower().Contains(search));
        }
        var invoices = await invoicesQuery.OrderByDescending(x => x.DeletedAt).Take(200)
            .Select(x => new { x.Id, x.InvoiceNumber, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
            .ToListAsync(cancellationToken);
        items.AddRange(invoices.Select(x => new RawDeletedItem(x.Id, "invoices", x.InvoiceNumber, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById)));

        // Products
        var productsQuery = db.Products.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
        if (!string.IsNullOrEmpty(search))
        {
            productsQuery = productsQuery.Where(x => x.Name.ToLower().Contains(search) || (x.Sku != null && x.Sku.ToLower().Contains(search)));
        }
        var products = await productsQuery.OrderByDescending(x => x.DeletedAt).Take(200)
            .Select(x => new { x.Id, x.Name, x.Sku, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
            .ToListAsync(cancellationToken);
        items.AddRange(products.Select(x => new RawDeletedItem(x.Id, "products", x.Name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById)));

        // Tickets
        var ticketsQuery = db.Tickets.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsDeleted && x.DeletedAt >= cutoff);
        if (!string.IsNullOrEmpty(search))
        {
            ticketsQuery = ticketsQuery.Where(x => x.Number.ToLower().Contains(search) || (x.Subject != null && x.Subject.ToLower().Contains(search)));
        }
        var tickets = await ticketsQuery.OrderByDescending(x => x.DeletedAt).Take(200)
            .Select(x => new { x.Id, x.Number, x.Subject, x.DeletedAt, x.UpdatedAt, x.CreatedAt, x.UpdatedById })
            .ToListAsync(cancellationToken);
        items.AddRange(tickets.Select(x =>
        {
            var name = !string.IsNullOrWhiteSpace(x.Subject) ? $"{x.Number}: {x.Subject}" : x.Number;
            return new RawDeletedItem(x.Id, "tickets", name, x.DeletedAt ?? x.UpdatedAt ?? x.CreatedAt, x.UpdatedById);
        }));

        return items;
    }

    public async Task<RecycleBinOperationResult> RestoreAsync(RestoreRecordsRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No tenant is set for this operation.");
        var entityType = request.EntityType?.Trim().ToLowerInvariant();
        var successIds = new List<Guid>();
        var errors = new List<string>();

        if (string.IsNullOrEmpty(entityType))
        {
            errors.Add("EntityType is required.");
            return new RecycleBinOperationResult(0, successIds, errors);
        }

        if (request.RecordIds == null || request.RecordIds.Count == 0)
        {
            return new RecycleBinOperationResult(0, successIds, errors);
        }

        var ids = request.RecordIds.Distinct().ToList();
        var now = clock.UtcNow;
        var currentUserId = tenant.UserId;

        switch (entityType)
        {
            case "leads":
            {
                var records = await db.Leads.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                foreach (var r in records)
                {
                    r.IsDeleted = false;
                    r.DeletedAt = null;
                    r.UpdatedAt = now;
                    r.UpdatedById = currentUserId;
                    successIds.Add(r.Id);
                    await timeline.PublishAsync(new TimelineEventInput(
                        "leads", r.Id, "record_restored",
                        $"Lead restored: {$"{r.FirstName} {r.LastName}".Trim()}",
                        "recycle_bin", null, currentUserId, now), cancellationToken);
                }
                break;
            }
            case "contacts":
            {
                var records = await db.Contacts.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                foreach (var r in records)
                {
                    r.IsDeleted = false;
                    r.DeletedAt = null;
                    r.UpdatedAt = now;
                    r.UpdatedById = currentUserId;
                    successIds.Add(r.Id);

                    var links = await db.ContactAccountLinks.IgnoreQueryFilters()
                        .Where(l => l.TenantId == tenantId && l.ContactId == r.Id && l.IsDeleted)
                        .ToListAsync(cancellationToken);
                    foreach (var l in links)
                    {
                        l.IsDeleted = false;
                        l.DeletedAt = null;
                        l.UpdatedAt = now;
                        l.UpdatedById = currentUserId;
                    }

                    await timeline.PublishAsync(new TimelineEventInput(
                        "contacts", r.Id, "record_restored",
                        $"Contact restored: {$"{r.FirstName} {r.LastName}".Trim()}",
                        "recycle_bin", null, currentUserId, now), cancellationToken);
                }
                break;
            }
            case "accounts":
            {
                var records = await db.Accounts.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                foreach (var r in records)
                {
                    r.IsDeleted = false;
                    r.DeletedAt = null;
                    r.UpdatedAt = now;
                    r.UpdatedById = currentUserId;
                    successIds.Add(r.Id);
                    await timeline.PublishAsync(new TimelineEventInput(
                        "accounts", r.Id, "record_restored",
                        $"Account restored: {r.Name}",
                        "recycle_bin", null, currentUserId, now), cancellationToken);
                }
                break;
            }
            case "deals":
            {
                var records = await db.Deals.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                foreach (var r in records)
                {
                    r.IsDeleted = false;
                    r.DeletedAt = null;
                    r.UpdatedAt = now;
                    r.UpdatedById = currentUserId;
                    successIds.Add(r.Id);

                    var contacts = await db.DealContacts.IgnoreQueryFilters()
                        .Where(dc => dc.TenantId == tenantId && dc.DealId == r.Id && dc.IsDeleted)
                        .ToListAsync(cancellationToken);
                    foreach (var dc in contacts)
                    {
                        dc.IsDeleted = false;
                        dc.DeletedAt = null;
                        dc.UpdatedAt = now;
                        dc.UpdatedById = currentUserId;
                    }

                    var lineItems = await db.DealLineItems.IgnoreQueryFilters()
                        .Where(li => li.TenantId == tenantId && li.DealId == r.Id && li.IsDeleted)
                        .ToListAsync(cancellationToken);
                    foreach (var li in lineItems)
                    {
                        li.IsDeleted = false;
                        li.DeletedAt = null;
                        li.UpdatedAt = now;
                        li.UpdatedById = currentUserId;
                    }

                    await timeline.PublishAsync(new TimelineEventInput(
                        "deals", r.Id, "record_restored",
                        $"Deal restored: {r.Name}",
                        "recycle_bin", null, currentUserId, now), cancellationToken);
                }
                break;
            }
            case "activities":
            {
                var records = await db.Activities.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                foreach (var r in records)
                {
                    r.IsDeleted = false;
                    r.DeletedAt = null;
                    r.UpdatedAt = now;
                    r.UpdatedById = currentUserId;
                    successIds.Add(r.Id);
                    await timeline.PublishAsync(new TimelineEventInput(
                        "activities", r.Id, "record_restored",
                        $"Activity restored: {r.Subject}",
                        "recycle_bin", null, currentUserId, now), cancellationToken);
                }
                break;
            }
            case "quotes":
            {
                var records = await db.Quotes.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                foreach (var r in records)
                {
                    r.IsDeleted = false;
                    r.DeletedAt = null;
                    r.UpdatedAt = now;
                    r.UpdatedById = currentUserId;
                    successIds.Add(r.Id);

                    var items = await db.QuoteLineItems.IgnoreQueryFilters()
                        .Where(li => li.TenantId == tenantId && li.QuoteId == r.Id && li.IsDeleted)
                        .ToListAsync(cancellationToken);
                    foreach (var li in items)
                    {
                        li.IsDeleted = false;
                        li.DeletedAt = null;
                        li.UpdatedAt = now;
                        li.UpdatedById = currentUserId;
                    }

                    await timeline.PublishAsync(new TimelineEventInput(
                        "quotes", r.Id, "record_restored",
                        $"Quote restored: {r.QuoteNumber}",
                        "recycle_bin", null, currentUserId, now), cancellationToken);
                }
                break;
            }
            case "invoices":
            {
                var records = await db.Invoices.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                foreach (var r in records)
                {
                    r.IsDeleted = false;
                    r.DeletedAt = null;
                    r.UpdatedAt = now;
                    r.UpdatedById = currentUserId;
                    successIds.Add(r.Id);

                    var items = await db.InvoiceLineItems.IgnoreQueryFilters()
                        .Where(li => li.TenantId == tenantId && li.InvoiceId == r.Id && li.IsDeleted)
                        .ToListAsync(cancellationToken);
                    foreach (var li in items)
                    {
                        li.IsDeleted = false;
                        li.DeletedAt = null;
                        li.UpdatedAt = now;
                        li.UpdatedById = currentUserId;
                    }

                    await timeline.PublishAsync(new TimelineEventInput(
                        "invoices", r.Id, "record_restored",
                        $"Invoice restored: {r.InvoiceNumber}",
                        "recycle_bin", null, currentUserId, now), cancellationToken);
                }
                break;
            }
            case "products":
            {
                var records = await db.Products.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                foreach (var r in records)
                {
                    r.IsDeleted = false;
                    r.DeletedAt = null;
                    r.UpdatedAt = now;
                    r.UpdatedById = currentUserId;
                    successIds.Add(r.Id);
                    await timeline.PublishAsync(new TimelineEventInput(
                        "products", r.Id, "record_restored",
                        $"Product restored: {r.Name}",
                        "recycle_bin", null, currentUserId, now), cancellationToken);
                }
                break;
            }
            case "tickets":
            {
                var records = await db.Tickets.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .ToListAsync(cancellationToken);
                foreach (var r in records)
                {
                    r.IsDeleted = false;
                    r.DeletedAt = null;
                    r.UpdatedAt = now;
                    r.UpdatedById = currentUserId;
                    successIds.Add(r.Id);

                    var comments = await db.TicketComments.IgnoreQueryFilters()
                        .Where(tc => tc.TenantId == tenantId && tc.TicketId == r.Id && tc.IsDeleted)
                        .ToListAsync(cancellationToken);
                    foreach (var tc in comments)
                    {
                        tc.IsDeleted = false;
                        tc.DeletedAt = null;
                        tc.UpdatedAt = now;
                        tc.UpdatedById = currentUserId;
                    }

                    await timeline.PublishAsync(new TimelineEventInput(
                        "tickets", r.Id, "record_restored",
                        $"Ticket restored: {r.Number}",
                        "recycle_bin", null, currentUserId, now), cancellationToken);
                }
                break;
            }
            default:
                errors.Add($"Unsupported entity type: '{request.EntityType}'.");
                return new RecycleBinOperationResult(0, successIds, errors);
        }

        if (successIds.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var missing = ids.Except(successIds).ToList();
        if (missing.Count > 0)
        {
            errors.Add($"{missing.Count} record(s) not found or already active.");
        }

        return new RecycleBinOperationResult(successIds.Count, successIds, errors);
    }

    public async Task<RecycleBinOperationResult> PurgeAsync(PurgeRecordsRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No tenant is set for this operation.");
        var entityType = request.EntityType?.Trim().ToLowerInvariant();
        var successIds = new List<Guid>();
        var errors = new List<string>();

        if (string.IsNullOrEmpty(entityType))
        {
            errors.Add("EntityType is required.");
            return new RecycleBinOperationResult(0, successIds, errors);
        }

        if (request.RecordIds == null || request.RecordIds.Count == 0)
        {
            return new RecycleBinOperationResult(0, successIds, errors);
        }

        var ids = request.RecordIds.Distinct().ToList();

        switch (entityType)
        {
            case "leads":
            {
                var matchedIds = await db.Leads.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                if (matchedIds.Count > 0)
                {
                    await db.Leads.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                    successIds.AddRange(matchedIds);
                }
                break;
            }
            case "contacts":
            {
                var matchedIds = await db.Contacts.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                if (matchedIds.Count > 0)
                {
                    await db.ContactAccountLinks.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.ContactId))
                        .ExecuteDeleteAsync(cancellationToken);
                    await db.Contacts.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                    successIds.AddRange(matchedIds);
                }
                break;
            }
            case "accounts":
            {
                var matchedIds = await db.Accounts.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                if (matchedIds.Count > 0)
                {
                    await db.Accounts.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                    successIds.AddRange(matchedIds);
                }
                break;
            }
            case "deals":
            {
                var matchedIds = await db.Deals.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                if (matchedIds.Count > 0)
                {
                    await db.DealContacts.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.DealId))
                        .ExecuteDeleteAsync(cancellationToken);
                    await db.DealLineItems.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.DealId))
                        .ExecuteDeleteAsync(cancellationToken);
                    await db.Deals.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                    successIds.AddRange(matchedIds);
                }
                break;
            }
            case "activities":
            {
                var matchedIds = await db.Activities.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                if (matchedIds.Count > 0)
                {
                    await db.Activities.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                    successIds.AddRange(matchedIds);
                }
                break;
            }
            case "quotes":
            {
                var matchedIds = await db.Quotes.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                if (matchedIds.Count > 0)
                {
                    await db.QuoteLineItems.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.QuoteId))
                        .ExecuteDeleteAsync(cancellationToken);
                    await db.Quotes.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                    successIds.AddRange(matchedIds);
                }
                break;
            }
            case "invoices":
            {
                var matchedIds = await db.Invoices.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                if (matchedIds.Count > 0)
                {
                    await db.InvoiceLineItems.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.InvoiceId))
                        .ExecuteDeleteAsync(cancellationToken);
                    await db.Payments.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.InvoiceId))
                        .ExecuteDeleteAsync(cancellationToken);
                    await db.Invoices.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                    successIds.AddRange(matchedIds);
                }
                break;
            }
            case "products":
            {
                var matchedIds = await db.Products.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                if (matchedIds.Count > 0)
                {
                    await db.Products.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                    successIds.AddRange(matchedIds);
                }
                break;
            }
            case "tickets":
            {
                var matchedIds = await db.Tickets.IgnoreQueryFilters()
                    .Where(x => x.TenantId == tenantId && x.IsDeleted && ids.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                if (matchedIds.Count > 0)
                {
                    await db.TicketComments.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.TicketId))
                        .ExecuteDeleteAsync(cancellationToken);
                    await db.Tickets.IgnoreQueryFilters()
                        .Where(x => x.TenantId == tenantId && matchedIds.Contains(x.Id))
                        .ExecuteDeleteAsync(cancellationToken);
                    successIds.AddRange(matchedIds);
                }
                break;
            }
            default:
                errors.Add($"Unsupported entity type: '{request.EntityType}'.");
                return new RecycleBinOperationResult(0, successIds, errors);
        }

        var missing = ids.Except(successIds).ToList();
        if (missing.Count > 0)
        {
            errors.Add($"{missing.Count} record(s) not found or cannot be purged.");
        }

        return new RecycleBinOperationResult(successIds.Count, successIds, errors);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.UtcNow.AddDays(-RetentionDays);
        var purgedCount = 0;

        purgedCount += await db.Leads.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.ContactAccountLinks.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.Contacts.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.Accounts.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.DealContacts.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.DealLineItems.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.Deals.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.Activities.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.QuoteLineItems.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.Quotes.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.InvoiceLineItems.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.Payments.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.Invoices.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.Products.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.TicketComments.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        purgedCount += await db.Tickets.IgnoreQueryFilters()
            .Where(x => x.IsDeleted && x.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return purgedCount;
    }
}
