using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

public sealed class SharedInboxService(
    AppDbContext db,
    ITenantContext tenantContext,
    ITimelineEventBus timeline) : ISharedInboxService
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase) { "Open", "Pending", "Closed", "Spam" };
    private static readonly HashSet<string> ValidPriorities = new(StringComparer.OrdinalIgnoreCase) { "Low", "Medium", "High", "Urgent" };

    public async Task<SharedMailboxDto> CreateMailboxAsync(CreateSharedMailboxRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("name", "Mailbox name is required.");

        if (string.IsNullOrWhiteSpace(request.EmailAddress))
            throw new ValidationException("emailAddress", "Email address is required.");

        var normalizedEmail = request.EmailAddress.Trim().ToLowerInvariant();

        var exists = await db.SharedMailboxes.AnyAsync(m => m.EmailAddress == normalizedEmail, ct);
        if (exists)
            throw new ValidationException("emailAddress", "A shared mailbox with this email address already exists.");

        var mailbox = new SharedMailbox
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            EmailAddress = normalizedEmail,
            Description = request.Description,
            IsActive = true,
            AutoAssignmentRule = string.IsNullOrWhiteSpace(request.AutoAssignmentRule) ? "Manual" : request.AutoAssignmentRule,
            DefaultAssigneeUserId = request.DefaultAssigneeUserId
        };

        db.SharedMailboxes.Add(mailbox);
        await db.SaveChangesAsync(ct);

        return new SharedMailboxDto(
            mailbox.Id,
            mailbox.Name,
            mailbox.EmailAddress,
            mailbox.Description,
            mailbox.IsActive,
            mailbox.AutoAssignmentRule,
            mailbox.DefaultAssigneeUserId,
            0,
            0);
    }

    public async Task<SharedMailboxDto> UpdateMailboxAsync(Guid id, UpdateSharedMailboxRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var mailbox = await db.SharedMailboxes.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mailbox == null)
            throw new NotFoundException("mailbox_not_found", "Shared mailbox was not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("name", "Mailbox name is required.");

        if (string.IsNullOrWhiteSpace(request.EmailAddress))
            throw new ValidationException("emailAddress", "Email address is required.");

        var normalizedEmail = request.EmailAddress.Trim().ToLowerInvariant();
        if (normalizedEmail != mailbox.EmailAddress)
        {
            var exists = await db.SharedMailboxes.AnyAsync(m => m.EmailAddress == normalizedEmail && m.Id != id, ct);
            if (exists)
                throw new ValidationException("emailAddress", "A shared mailbox with this email address already exists.");
            mailbox.EmailAddress = normalizedEmail;
        }

        mailbox.Name = request.Name.Trim();
        mailbox.Description = request.Description;
        mailbox.IsActive = request.IsActive;
        mailbox.AutoAssignmentRule = request.AutoAssignmentRule;
        mailbox.DefaultAssigneeUserId = request.DefaultAssigneeUserId;

        await db.SaveChangesAsync(ct);

        var memberCount = await db.SharedMailboxMembers.CountAsync(m => m.MailboxId == id, ct);
        var openThreadCount = await db.SharedThreads.CountAsync(t => t.MailboxId == id && t.Status == "Open", ct);

        return new SharedMailboxDto(
            mailbox.Id,
            mailbox.Name,
            mailbox.EmailAddress,
            mailbox.Description,
            mailbox.IsActive,
            mailbox.AutoAssignmentRule,
            mailbox.DefaultAssigneeUserId,
            memberCount,
            openThreadCount);
    }

    public async Task<IReadOnlyList<SharedMailboxDto>> GetMailboxesAsync(CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var mailboxes = await db.SharedMailboxes
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

        var memberCounts = await db.SharedMailboxMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .GroupBy(m => m.MailboxId)
            .Select(g => new { MailboxId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MailboxId, x => x.Count, ct);

        var openThreadCounts = await db.SharedThreads
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Status == "Open")
            .GroupBy(t => t.MailboxId)
            .Select(g => new { MailboxId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MailboxId, x => x.Count, ct);

        return mailboxes.Select(m => new SharedMailboxDto(
            m.Id,
            m.Name,
            m.EmailAddress,
            m.Description,
            m.IsActive,
            m.AutoAssignmentRule,
            m.DefaultAssigneeUserId,
            memberCounts.GetValueOrDefault(m.Id, 0),
            openThreadCounts.GetValueOrDefault(m.Id, 0))).ToList();
    }

    public async Task<SharedMailboxDto> GetMailboxAsync(Guid id, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var mailbox = await db.SharedMailboxes.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mailbox == null)
            throw new NotFoundException("mailbox_not_found", "Shared mailbox was not found.");

        var memberCount = await db.SharedMailboxMembers.CountAsync(m => m.MailboxId == id, ct);
        var openThreadCount = await db.SharedThreads.CountAsync(t => t.MailboxId == id && t.Status == "Open", ct);

        return new SharedMailboxDto(
            mailbox.Id,
            mailbox.Name,
            mailbox.EmailAddress,
            mailbox.Description,
            mailbox.IsActive,
            mailbox.AutoAssignmentRule,
            mailbox.DefaultAssigneeUserId,
            memberCount,
            openThreadCount);
    }

    public async Task<SharedMailboxMemberDto> AddMemberAsync(Guid mailboxId, AddMailboxMemberRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var mailbox = await db.SharedMailboxes.FirstOrDefaultAsync(m => m.Id == mailboxId, ct);
        if (mailbox == null)
            throw new NotFoundException("mailbox_not_found", "Shared mailbox was not found.");

        var member = await db.SharedMailboxMembers.FirstOrDefaultAsync(m => m.MailboxId == mailboxId && m.UserId == request.UserId, ct);
        if (member == null)
        {
            member = new SharedMailboxMember
            {
                TenantId = tenantId,
                MailboxId = mailboxId,
                UserId = request.UserId,
                Role = string.IsNullOrWhiteSpace(request.Role) ? "Agent" : request.Role
            };
            db.SharedMailboxMembers.Add(member);
        }
        else
        {
            member.Role = string.IsNullOrWhiteSpace(request.Role) ? member.Role : request.Role;
        }

        await db.SaveChangesAsync(ct);
        return new SharedMailboxMemberDto(member.Id, member.MailboxId, member.UserId, member.Role);
    }

    public async Task<bool> RemoveMemberAsync(Guid mailboxId, Guid userId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var member = await db.SharedMailboxMembers.FirstOrDefaultAsync(m => m.MailboxId == mailboxId && m.UserId == userId, ct);
        if (member == null) return false;

        db.SharedMailboxMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<SharedMailboxMemberDto>> GetMembersAsync(Guid mailboxId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var members = await db.SharedMailboxMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.MailboxId == mailboxId)
            .ToListAsync(ct);

        return members.Select(m => new SharedMailboxMemberDto(m.Id, m.MailboxId, m.UserId, m.Role)).ToList();
    }

    public async Task<SharedThreadDto> CreateThreadAsync(CreateSharedThreadRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var mailbox = await db.SharedMailboxes.FirstOrDefaultAsync(m => m.Id == request.MailboxId, ct);
        if (mailbox == null)
            throw new NotFoundException("mailbox_not_found", "Shared mailbox was not found.");

        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new ValidationException("subject", "Subject is required.");

        if (string.IsNullOrWhiteSpace(request.CustomerEmail))
            throw new ValidationException("customerEmail", "Customer email is required.");

        var priority = ValidPriorities.Contains(request.Priority) ? request.Priority : "Medium";

        // Assignment logic
        Guid? assignedUserId = null;
        if (string.Equals(mailbox.AutoAssignmentRule, "RoundRobin", StringComparison.OrdinalIgnoreCase))
        {
            var agentMembers = await db.SharedMailboxMembers
                .Where(m => m.MailboxId == mailbox.Id && m.Role != "Viewer")
                .Select(m => m.UserId)
                .ToListAsync(ct);

            if (agentMembers.Count > 0)
            {
                // Find agent with fewest open threads in this mailbox
                var agentThreadCounts = await db.SharedThreads
                    .Where(t => t.MailboxId == mailbox.Id && t.Status == "Open" && t.AssignedUserId != null && agentMembers.Contains(t.AssignedUserId.Value))
                    .GroupBy(t => t.AssignedUserId!.Value)
                    .Select(g => new { UserId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

                assignedUserId = agentMembers.OrderBy(uid => agentThreadCounts.GetValueOrDefault(uid, 0)).First();
            }
        }

        assignedUserId ??= mailbox.DefaultAssigneeUserId;

        // Auto-link contact/lead if not specified
        var contactId = request.ContactId;
        var leadId = request.LeadId;
        var accountId = request.AccountId;
        var dealId = request.DealId;

        if (contactId == null && leadId == null)
        {
            var norm = request.CustomerEmail.Trim().ToLowerInvariant();
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.NormalizedEmail == norm || c.Email == norm, ct);
            if (contact != null)
            {
                contactId = contact.Id;
                accountId ??= contact.AccountId;
            }
            else
            {
                var lead = await db.Leads.FirstOrDefaultAsync(l => l.NormalizedEmail == norm || l.Email == norm, ct);
                if (lead != null)
                {
                    leadId = lead.Id;
                }
            }
        }

        var thread = new SharedThread
        {
            TenantId = tenantId,
            MailboxId = mailbox.Id,
            Subject = request.Subject.Trim(),
            CustomerEmail = request.CustomerEmail.Trim().ToLowerInvariant(),
            CustomerName = request.CustomerName,
            Status = "Open",
            Priority = priority,
            AssignedUserId = assignedUserId,
            AssignedAtUtc = assignedUserId != null ? DateTime.UtcNow : null,
            LastMessageAtUtc = DateTime.UtcNow,
            ContactId = contactId,
            LeadId = leadId,
            AccountId = accountId,
            DealId = dealId,
            MessageCount = !string.IsNullOrWhiteSpace(request.Body) ? 1 : 0
        };

        db.SharedThreads.Add(thread);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(request.Body))
        {
            var initialMsg = new SharedThreadMessage
            {
                TenantId = tenantId,
                ThreadId = thread.Id,
                SenderType = "Customer",
                SenderEmail = thread.CustomerEmail,
                SenderName = thread.CustomerName,
                Body = request.Body,
                IsInternalNote = false,
                SentAtUtc = DateTime.UtcNow
            };
            db.SharedThreadMessages.Add(initialMsg);
            await db.SaveChangesAsync(ct);
        }

        await PublishTimelineAsync(leadId, contactId, dealId, "shared_thread_created", $"New inquiry in {mailbox.Name}: {thread.Subject}", ct);

        return MapThreadDto(thread);
    }

    public async Task<IReadOnlyList<SharedThreadDto>> GetThreadsAsync(SharedThreadQuery query, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var q = db.SharedThreads.AsNoTracking().Where(t => t.TenantId == tenantId);

        if (query.MailboxId.HasValue) q = q.Where(t => t.MailboxId == query.MailboxId.Value);
        if (query.AssignedUserId.HasValue) q = q.Where(t => t.AssignedUserId == query.AssignedUserId.Value);
        if (query.UnassignedOnly == true) q = q.Where(t => t.AssignedUserId == null);
        if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(t => t.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Priority)) q = q.Where(t => t.Priority == query.Priority);

        var list = await q.OrderByDescending(t => t.LastMessageAtUtc)
            .Take(Math.Clamp(query.Limit, 1, 200))
            .ToListAsync(ct);

        return list.Select(MapThreadDto).ToList();
    }

    public async Task<SharedThreadDto> GetThreadAsync(Guid id, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var thread = await db.SharedThreads.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (thread == null)
            throw new NotFoundException("thread_not_found", "Thread was not found.");

        return MapThreadDto(thread);
    }

    public async Task<SharedThreadDto> AssignThreadAsync(Guid id, AssignThreadRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var thread = await db.SharedThreads.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (thread == null)
            throw new NotFoundException("thread_not_found", "Thread was not found.");

        thread.AssignedUserId = request.AssignedUserId;
        thread.AssignedAtUtc = request.AssignedUserId != null ? DateTime.UtcNow : null;

        await db.SaveChangesAsync(ct);

        await PublishTimelineAsync(thread.LeadId, thread.ContactId, thread.DealId, "shared_thread_assigned", $"Thread '{thread.Subject}' reassigned", ct);

        return MapThreadDto(thread);
    }

    public async Task<SharedThreadDto> UpdateThreadStatusAsync(Guid id, UpdateThreadStatusRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        if (!ValidStatuses.Contains(request.Status))
            throw new ValidationException("status", "Invalid status value.");

        var thread = await db.SharedThreads.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (thread == null)
            throw new NotFoundException("thread_not_found", "Thread was not found.");

        var prevStatus = thread.Status;
        thread.Status = request.Status;

        if (string.Equals(request.Status, "Closed", StringComparison.OrdinalIgnoreCase) && !string.Equals(prevStatus, "Closed", StringComparison.OrdinalIgnoreCase))
        {
            thread.ClosedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        await PublishTimelineAsync(thread.LeadId, thread.ContactId, thread.DealId, "shared_thread_status_changed", $"Thread '{thread.Subject}' marked as {thread.Status}", ct);

        return MapThreadDto(thread);
    }

    public async Task<SharedThreadMessageDto> PostMessageAsync(Guid threadId, PostThreadMessageRequest request, Guid currentUserId, string currentUserName, string currentUserEmail, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        if (string.IsNullOrWhiteSpace(request.Body))
            throw new ValidationException("body", "Message body is required.");

        var thread = await db.SharedThreads.FirstOrDefaultAsync(t => t.Id == threadId, ct);
        if (thread == null)
            throw new NotFoundException("thread_not_found", "Thread was not found.");

        var now = DateTime.UtcNow;
        thread.LastMessageAtUtc = now;
        thread.MessageCount++;

        if (!request.IsInternalNote && thread.FirstResponseAtUtc == null)
        {
            thread.FirstResponseAtUtc = now;
        }

        var message = new SharedThreadMessage
        {
            TenantId = tenantId,
            ThreadId = thread.Id,
            SenderType = "Agent",
            SenderEmail = currentUserEmail,
            SenderName = currentUserName,
            SenderUserId = currentUserId,
            Body = request.Body,
            IsInternalNote = request.IsInternalNote,
            SentAtUtc = now
        };

        db.SharedThreadMessages.Add(message);
        await db.SaveChangesAsync(ct);

        var eventKind = request.IsInternalNote ? "shared_thread_internal_note" : "shared_thread_reply_sent";
        var eventSummary = request.IsInternalNote ? $"Internal note added to thread '{thread.Subject}'" : $"Reply sent to {thread.CustomerEmail} for thread '{thread.Subject}'";
        await PublishTimelineAsync(thread.LeadId, thread.ContactId, thread.DealId, eventKind, eventSummary, ct);

        return MapMessageDto(message);
    }

    public async Task<IReadOnlyList<SharedThreadMessageDto>> GetThreadMessagesAsync(Guid threadId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var threadExists = await db.SharedThreads.AnyAsync(t => t.Id == threadId, ct);
        if (!threadExists)
            throw new NotFoundException("thread_not_found", "Thread was not found.");

        var messages = await db.SharedThreadMessages
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.ThreadId == threadId)
            .OrderBy(m => m.SentAtUtc)
            .ToListAsync(ct);

        return messages.Select(MapMessageDto).ToList();
    }

    public async Task<CollisionStatusDto> AcquireLockAsync(Guid threadId, AcquireLockRequest request, Guid currentUserId, string currentUserName, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var threadExists = await db.SharedThreads.AnyAsync(t => t.Id == threadId, ct);
        if (!threadExists)
            throw new NotFoundException("thread_not_found", "Thread was not found.");

        var now = DateTime.UtcNow;

        // Check if another active lock exists
        var existingLock = await db.SharedThreadCollisionLocks
            .FirstOrDefaultAsync(l => l.ThreadId == threadId && l.UserId != currentUserId && l.ExpiresAtUtc > now, ct);

        if (existingLock != null)
        {
            return new CollisionStatusDto(
                threadId,
                HasCollision: true,
                LockedByUserId: existingLock.UserId,
                LockedByUserName: existingLock.UserName,
                Action: existingLock.Action,
                ExpiresAtUtc: existingLock.ExpiresAtUtc);
        }

        // Acquire or refresh own lock
        var myLock = await db.SharedThreadCollisionLocks
            .FirstOrDefaultAsync(l => l.ThreadId == threadId && l.UserId == currentUserId, ct);

        var expiresAt = now.AddMinutes(1);
        var action = string.IsNullOrWhiteSpace(request.Action) ? "Drafting" : request.Action;

        if (myLock == null)
        {
            myLock = new SharedThreadCollisionLock
            {
                TenantId = tenantId,
                ThreadId = threadId,
                UserId = currentUserId,
                UserName = currentUserName,
                Action = action,
                LockedAtUtc = now,
                ExpiresAtUtc = expiresAt
            };
            db.SharedThreadCollisionLocks.Add(myLock);
        }
        else
        {
            myLock.Action = action;
            myLock.UserName = currentUserName;
            myLock.ExpiresAtUtc = expiresAt;
        }

        await db.SaveChangesAsync(ct);

        return new CollisionStatusDto(
            threadId,
            HasCollision: false,
            LockedByUserId: currentUserId,
            LockedByUserName: currentUserName,
            Action: action,
            ExpiresAtUtc: expiresAt);
    }

    public async Task<bool> ReleaseLockAsync(Guid threadId, Guid currentUserId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var myLock = await db.SharedThreadCollisionLocks
            .FirstOrDefaultAsync(l => l.ThreadId == threadId && l.UserId == currentUserId, ct);

        if (myLock == null) return false;

        db.SharedThreadCollisionLocks.Remove(myLock);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<CollisionStatusDto> GetCollisionStatusAsync(Guid threadId, Guid currentUserId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var now = DateTime.UtcNow;
        var otherLock = await db.SharedThreadCollisionLocks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.ThreadId == threadId && l.UserId != currentUserId && l.ExpiresAtUtc > now, ct);

        if (otherLock != null)
        {
            return new CollisionStatusDto(
                threadId,
                HasCollision: true,
                LockedByUserId: otherLock.UserId,
                LockedByUserName: otherLock.UserName,
                Action: otherLock.Action,
                ExpiresAtUtc: otherLock.ExpiresAtUtc);
        }

        return new CollisionStatusDto(
            threadId,
            HasCollision: false,
            LockedByUserId: null,
            LockedByUserName: null,
            Action: null,
            ExpiresAtUtc: null);
    }

    private async Task PublishTimelineAsync(Guid? leadId, Guid? contactId, Guid? dealId, string kind, string summary, CancellationToken ct)
    {
        try
        {
            if (leadId != null)
            {
                await timeline.PublishAsync(new TimelineEventInput("lead", leadId.Value, kind, summary, "shared-inbox"), ct);
            }
            else if (contactId != null)
            {
                await timeline.PublishAsync(new TimelineEventInput("contact", contactId.Value, kind, summary, "shared-inbox"), ct);
            }

            if (dealId != null)
            {
                await timeline.PublishAsync(new TimelineEventInput("deal", dealId.Value, kind, summary, "shared-inbox"), ct);
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    private static SharedMailboxDto MapMailboxDto(SharedMailbox m, int memberCount, int openThreadCount) => new(
        m.Id,
        m.Name,
        m.EmailAddress,
        m.Description,
        m.IsActive,
        m.AutoAssignmentRule,
        m.DefaultAssigneeUserId,
        memberCount,
        openThreadCount);

    private static SharedThreadDto MapThreadDto(SharedThread t) => new(
        t.Id,
        t.MailboxId,
        t.Subject,
        t.CustomerEmail,
        t.CustomerName,
        t.Status,
        t.Priority,
        t.AssignedUserId,
        t.AssignedAtUtc,
        t.LastMessageAtUtc,
        t.FirstResponseAtUtc,
        t.ClosedAtUtc,
        t.LeadId,
        t.ContactId,
        t.AccountId,
        t.DealId,
        t.MessageCount);

    private static SharedThreadMessageDto MapMessageDto(SharedThreadMessage m) => new(
        m.Id,
        m.ThreadId,
        m.SenderType,
        m.SenderEmail,
        m.SenderName,
        m.SenderUserId,
        m.Body,
        m.IsInternalNote,
        m.SentAtUtc);
}
