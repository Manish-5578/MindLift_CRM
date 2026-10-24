using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Contacts;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Tenancy;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Tickets;

/// <summary>
/// The help desk: customers' requests, who looks after each, how late it is, and the conversation on it.
/// A person sees the tickets their data scope allows: their own (assigned to them or opened by them), their team's, or all.
/// </summary>
public sealed class TicketService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    IDataScopeService scope,
    NumberSequences numbers,
    INotificationService notifications,
    IEmailSender email,
    MindLift.Infrastructure.Messaging.EmailGuard emailGuard)
{
    private Guid CurrentTenantId => tenant.TenantId ?? throw new UnauthorizedAccessException("Tenant context required.");

    // ------------------------------------------------------------------ reading

    private async Task<IQueryable<Ticket>> ScopedAsync(CancellationToken cancellationToken)
    {
        var source = db.Tickets.AsNoTracking();
        var users = await scope.GetScopeUserIdsAsync(AppModule.Tickets, cancellationToken);
        return users is null ? source : source.Where(t => (t.AssigneeId != null && users.Contains(t.AssigneeId.Value)) || (t.CreatedById != null && users.Contains(t.CreatedById.Value)));
    }

    public async Task<PagedResult<TicketListItemDto>> QueryAsync(TicketQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var now = clock.UtcNow;
        var source = await ScopedAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(query.Status)) source = source.Where(t => t.Status == query.Status);
        else if (!query.LateOnly) { /* all statuses */ }
        if (!string.IsNullOrWhiteSpace(query.Priority)) source = source.Where(t => t.Priority == query.Priority);
        if (query.AssigneeId is { } who) source = source.Where(t => t.AssigneeId == who);
        if (query.MineOnly && tenant.UserId is { } me) source = source.Where(t => t.AssigneeId == me);
        if (query.UnassignedOnly) source = source.Where(t => t.AssigneeId == null && (t.Status == TicketStatuses.Open || t.Status == TicketStatuses.Pending));
        if (query.LateOnly) source = source.Where(t => t.DueAt != null && t.DueAt < now && t.FirstResponseAt == null && (t.Status == TicketStatuses.Open || t.Status == TicketStatuses.Pending));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(t => t.Number.ToLower().Contains(term) || t.Subject.ToLower().Contains(term) || (t.Description != null && t.Description.ToLower().Contains(term)));
        }

        var total = await source.CountAsync(cancellationToken);
        var rows = await source.OrderByDescending(t => t.LastActivityAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var (contacts, accounts, users) = await NamesAsync(rows, cancellationToken);
        var items = rows.Select(t => new TicketListItemDto(t.Id, t.Number, t.Subject, t.Status, t.Priority,
            t.ContactId is { } c ? contacts.GetValueOrDefault(c) : null, t.AccountId is { } a ? accounts.GetValueOrDefault(a) : null,
            t.AssigneeId, t.AssigneeId is { } u ? users.GetValueOrDefault(u) : null, t.DueAt, IsLate(t, now), t.LastActivityAt, t.CreatedAt)).ToList();
        return new PagedResult<TicketListItemDto>(items, total, page, pageSize);
    }

    public async Task<TicketDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadScopedAsync(id, cancellationToken);
        return await MapAsync(ticket, cancellationToken);
    }

    public async Task<TicketSummaryDto> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var source = await ScopedAsync(cancellationToken);
        var active = source.Where(t => t.Status == TicketStatuses.Open || t.Status == TicketStatuses.Pending);
        var week = now.AddDays(-7);
        var answered = await source.Where(t => t.FirstResponseAt != null && t.CreatedAt >= now.AddDays(-30))
            .Select(t => new { t.CreatedAt, t.FirstResponseAt }).ToListAsync(cancellationToken);
        var byPriority = await active.GroupBy(t => t.Priority).Select(g => new { g.Key, N = g.Count() }).ToListAsync(cancellationToken);

        return new TicketSummaryDto(
            await source.CountAsync(t => t.Status == TicketStatuses.Open, cancellationToken),
            await source.CountAsync(t => t.Status == TicketStatuses.Pending, cancellationToken),
            await active.CountAsync(t => t.DueAt != null && t.DueAt < now && t.FirstResponseAt == null, cancellationToken),
            await active.CountAsync(t => t.AssigneeId == null, cancellationToken),
            await source.CountAsync(t => t.ResolvedAt != null && t.ResolvedAt >= week, cancellationToken),
            answered.Count == 0 ? null : Math.Round(answered.Average(t => (t.FirstResponseAt!.Value - t.CreatedAt).TotalHours), 1),
            TicketPriorities.All.Select(p => new PlatformSlice(p, p, byPriority.FirstOrDefault(x => x.Key == p)?.N ?? 0)).ToList());
    }

    // ------------------------------------------------------------------ writing

    public async Task<TicketDto> CreateAsync(SaveTicketRequest request, string channel = "manual", CancellationToken cancellationToken = default)
    {
        var subject = Validate(request);
        await CheckLinksAsync(request, cancellationToken);
        var priority = string.IsNullOrWhiteSpace(request.Priority) ? TicketPriorities.Normal : request.Priority.Trim().ToLowerInvariant();
        var now = clock.UtcNow;
        var number = await numbers.NextAsync("ticket", async () => 10001 + await db.Tickets.IgnoreQueryFilters().CountAsync(t => t.TenantId == CurrentTenantId, cancellationToken), cancellationToken);

        var ticket = new Ticket
        {
            Number = $"T-{number}",
            Subject = subject,
            Description = RecordHelpers.Clean(request.Description),
            Priority = priority,
            Category = RecordHelpers.Clean(request.Category),
            Channel = channel,
            ContactId = request.ContactId,
            AccountId = request.AccountId,
            AssigneeId = request.AssigneeId,
            DueAt = now + TicketPriorities.AnswerWithin(priority),
            LastActivityAt = now,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken);
        await TellAssigneeAsync(ticket, cancellationToken);
        return await MapAsync(ticket, cancellationToken);
    }

    public async Task<TicketDto> UpdateAsync(Guid id, SaveTicketRequest request, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadScopedForWriteAsync(id, cancellationToken);
        ticket.Subject = Validate(request);
        await CheckLinksAsync(request, cancellationToken);
        ticket.Description = RecordHelpers.Clean(request.Description);
        ticket.Category = RecordHelpers.Clean(request.Category);
        ticket.ContactId = request.ContactId;
        ticket.AccountId = request.AccountId;

        var priority = string.IsNullOrWhiteSpace(request.Priority) ? ticket.Priority : request.Priority.Trim().ToLowerInvariant();
        if (priority != ticket.Priority)
        {
            ticket.Priority = priority;
            if (ticket.FirstResponseAt is null) ticket.DueAt = ticket.CreatedAt + TicketPriorities.AnswerWithin(priority);
        }

        var assigneeChanged = request.AssigneeId != ticket.AssigneeId;
        ticket.AssigneeId = request.AssigneeId;
        ticket.LastActivityAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (assigneeChanged) await TellAssigneeAsync(ticket, cancellationToken);
        return await MapAsync(ticket, cancellationToken);
    }

    public async Task<TicketDto> AssignAsync(Guid id, AssignTicketRequest request, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadScopedForWriteAsync(id, cancellationToken);
        if (request.AssigneeId is { } who) await RecordHelpers.EnsureOwnerAsync(db, who, cancellationToken);
        var changed = ticket.AssigneeId != request.AssigneeId;
        ticket.AssigneeId = request.AssigneeId;
        ticket.LastActivityAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (changed) await TellAssigneeAsync(ticket, cancellationToken);
        return await MapAsync(ticket, cancellationToken);
    }

    public async Task<TicketDto> ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request, CancellationToken cancellationToken = default)
    {
        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (!TicketStatuses.All.Contains(status))
        {
            throw new ValidationException("status", "Choose open, pending, resolved or closed.");
        }

        var ticket = await LoadScopedForWriteAsync(id, cancellationToken);
        var now = clock.UtcNow;
        ticket.Status = status;
        ticket.ResolvedAt = TicketStatuses.IsFinished(status) ? ticket.ResolvedAt ?? now : null;
        ticket.LastActivityAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return await MapAsync(ticket, cancellationToken);
    }

    public async Task<TicketDto> AddCommentAsync(Guid id, AddTicketCommentRequest request, CancellationToken cancellationToken = default)
    {
        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length is 0 or > 10000)
        {
            throw new ValidationException("body", "Write a message of up to 10,000 characters.");
        }

        var ticket = await LoadScopedForWriteAsync(id, cancellationToken);
        var now = clock.UtcNow;
        var author = await AuthorNameAsync(cancellationToken);
        db.TicketComments.Add(new TicketComment { TicketId = ticket.Id, AuthorId = tenant.UserId, AuthorName = author, Body = body, IsInternal = request.IsInternal });
        ticket.LastActivityAt = now;

        // The first public answer stops the clock on the answer time.
        if (!request.IsInternal && ticket.FirstResponseAt is null)
        {
            ticket.FirstResponseAt = now;
        }

        // Answering a ticket nobody has taken makes it the answerer's.
        if (ticket.AssigneeId is null && tenant.UserId is { } me)
        {
            ticket.AssigneeId = me;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (request.EmailCustomer && !request.IsInternal)
        {
            await EmailCustomerAsync(ticket, body, author, cancellationToken);
        }

        return await MapAsync(ticket, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadScopedForWriteAsync(id, cancellationToken);
        db.Tickets.Remove(ticket);
        await db.SaveChangesAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Ticket> LoadScopedAsync(Guid id, CancellationToken cancellationToken)
        => await (await ScopedAsync(cancellationToken)).FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException("Ticket", id);

    private async Task<Ticket> LoadScopedForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        await LoadScopedAsync(id, cancellationToken);   // refuses a ticket outside the person's scope
        return await db.Tickets.FirstAsync(t => t.Id == id, cancellationToken);
    }

    private static string Validate(SaveTicketRequest request)
    {
        var subject = (request.Subject ?? string.Empty).Trim();
        if (subject.Length is 0 or > 200)
        {
            throw new ValidationException("subject", "Enter a subject of up to 200 characters.");
        }

        if (request.Priority is { Length: > 0 } p && !TicketPriorities.All.Contains(p.Trim().ToLowerInvariant()))
        {
            throw new ValidationException("priority", "Choose low, normal, high or urgent.");
        }

        return subject;
    }

    private async Task CheckLinksAsync(SaveTicketRequest request, CancellationToken cancellationToken)
    {
        if (request.AssigneeId is { } who) await RecordHelpers.EnsureOwnerAsync(db, who, cancellationToken);
        if (request.ContactId is { } c && !await db.Contacts.AnyAsync(x => x.Id == c, cancellationToken)) throw new ValidationException("contactId", "That contact does not exist.");
        if (request.AccountId is { } a && !await db.Accounts.AnyAsync(x => x.Id == a, cancellationToken)) throw new ValidationException("accountId", "That account does not exist.");
    }

    private static bool IsLate(Ticket t, DateTime now)
        => t.DueAt is { } due && due < now && t.FirstResponseAt is null && (t.Status == TicketStatuses.Open || t.Status == TicketStatuses.Pending);

    private async Task<(Dictionary<Guid, string> Contacts, Dictionary<Guid, string> Accounts, Dictionary<Guid, string> Users)> NamesAsync(IReadOnlyList<Ticket> rows, CancellationToken cancellationToken)
    {
        var contactIds = rows.Where(r => r.ContactId != null).Select(r => r.ContactId!.Value).Distinct().ToList();
        var accountIds = rows.Where(r => r.AccountId != null).Select(r => r.AccountId!.Value).Distinct().ToList();
        var contacts = await db.Contacts.AsNoTracking().Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => $"{c.FirstName} {c.LastName}".Trim(), cancellationToken);
        var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        var users = await RecordHelpers.OwnerNamesAsync(db, rows.Where(r => r.AssigneeId != null).Select(r => r.AssigneeId!.Value).Distinct(), cancellationToken);
        return (contacts, accounts, users);
    }

    private async Task<TicketDto> MapAsync(Ticket t, CancellationToken cancellationToken)
    {
        var (contacts, accounts, users) = await NamesAsync([t], cancellationToken);
        var email = t.ContactId is { } c ? await db.Contacts.AsNoTracking().Where(x => x.Id == c).Select(x => x.Email).FirstOrDefaultAsync(cancellationToken) : null;
        var comments = await db.TicketComments.AsNoTracking().Where(x => x.TicketId == t.Id).OrderBy(x => x.CreatedAt)
            .Select(x => new TicketCommentDto(x.Id, x.AuthorName, x.Body, x.IsInternal, x.CreatedAt)).ToListAsync(cancellationToken);
        return new TicketDto(t.Id, t.Number, t.Subject, t.Description, t.Status, t.Priority, t.Category, t.Channel,
            t.ContactId, t.ContactId is { } ci ? contacts.GetValueOrDefault(ci) : null, email,
            t.AccountId, t.AccountId is { } ai ? accounts.GetValueOrDefault(ai) : null,
            t.AssigneeId, t.AssigneeId is { } ui ? users.GetValueOrDefault(ui) : null,
            t.DueAt, IsLate(t, clock.UtcNow), t.FirstResponseAt, t.ResolvedAt, t.CreatedAt, t.LastActivityAt, comments);
    }

    private async Task<string> AuthorNameAsync(CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } id) return "Support";
        var name = (await RecordHelpers.OwnerNamesAsync(db, [id], cancellationToken)).GetValueOrDefault(id);
        return string.IsNullOrWhiteSpace(name) ? "Support" : name;
    }

    private async Task TellAssigneeAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        if (ticket.AssigneeId is not { } who || who == tenant.UserId)
        {
            return;
        }

        await notifications.NotifyAsync(new NotificationRequest([who], NotificationTypes.TicketAssigned, $"Ticket {ticket.Number} is assigned to you", ticket.Subject,
            "Ticket", ticket.Id, $"/tickets/{ticket.Id}", IncludeActor: false), cancellationToken);
    }

    /// <summary>A public reply goes to the customer by email, in the company's name, unless they asked not to be emailed.</summary>
    private async Task EmailCustomerAsync(Ticket ticket, string body, string author, CancellationToken cancellationToken)
    {
        var address = ticket.ContactId is { } c ? await db.Contacts.AsNoTracking().Where(x => x.Id == c).Select(x => x.Email).FirstOrDefaultAsync(cancellationToken) : null;
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new BusinessRuleException("no_contact_email", "This ticket has no contact with an email address to reply to.");
        }

        var (allowed, reason) = await emailGuard.TryReserveAsync(address, cancellationToken);
        if (!allowed)
        {
            throw new BusinessRuleException("email_not_allowed", reason ?? "This address cannot be emailed.");
        }

        await email.SendAsync(address, $"Re: {ticket.Subject} [{ticket.Number}]", $"{body}\n\n-- {author}\nReference: {ticket.Number}{emailGuard.Footer(address)}", cancellationToken);
    }
}
