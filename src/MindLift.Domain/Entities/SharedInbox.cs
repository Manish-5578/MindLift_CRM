using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 5 / #35: Shared team inboxes (shared-inbox).
/// Centralized team mailbox (e.g. sales@, support@) accessible by multiple agents.
/// </summary>
[Audited]
public sealed class SharedMailbox : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string AutoAssignmentRule { get; set; } = "Manual"; // "Manual", "RoundRobin", "LoadBalanced"
    public Guid? DefaultAssigneeUserId { get; set; }
}

/// <summary>
/// Phase 5 / #35: Team member assignment and role in a shared mailbox.
/// </summary>
[Audited]
public sealed class SharedMailboxMember : TenantEntity
{
    public Guid MailboxId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "Agent"; // "Admin", "Agent", "Viewer"
}

/// <summary>
/// Phase 5 / #35: Conversation thread in a shared team inbox.
/// </summary>
[Audited]
public sealed class SharedThread : TenantEntity
{
    public Guid MailboxId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string Status { get; set; } = "Open"; // "Open", "Pending", "Closed", "Spam"
    public string Priority { get; set; } = "Medium"; // "Low", "Medium", "High", "Urgent"
    public Guid? AssignedUserId { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FirstResponseAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
    public int MessageCount { get; set; }
}

/// <summary>
/// Phase 5 / #35: Messages and private internal agent notes within a shared thread.
/// </summary>
[Audited]
public sealed class SharedThreadMessage : TenantEntity
{
    public Guid ThreadId { get; set; }
    public string SenderType { get; set; } = "Customer"; // "Customer", "Agent", "System"
    public string SenderEmail { get; set; } = string.Empty;
    public string? SenderName { get; set; }
    public Guid? SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsInternalNote { get; set; }
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Phase 5 / #35: Collision lock to prevent two agents drafting replies simultaneously.
/// </summary>
[Audited]
public sealed class SharedThreadCollisionLock : TenantEntity
{
    public Guid ThreadId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = "Drafting"; // "Viewing", "Drafting"
    public DateTime LockedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(1);
}
