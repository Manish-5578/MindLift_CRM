namespace MindLift.Shared.Contracts;

public sealed record CreateSharedMailboxRequest(
    string Name,
    string EmailAddress,
    string? Description = null,
    string AutoAssignmentRule = "Manual",
    Guid? DefaultAssigneeUserId = null);

public sealed record UpdateSharedMailboxRequest(
    string Name,
    string EmailAddress,
    string? Description = null,
    bool IsActive = true,
    string AutoAssignmentRule = "Manual",
    Guid? DefaultAssigneeUserId = null);

public sealed record SharedMailboxDto(
    Guid Id,
    string Name,
    string EmailAddress,
    string? Description,
    bool IsActive,
    string AutoAssignmentRule,
    Guid? DefaultAssigneeUserId,
    int MemberCount,
    int OpenThreadCount);

public sealed record AddMailboxMemberRequest(
    Guid UserId,
    string Role = "Agent");

public sealed record SharedMailboxMemberDto(
    Guid Id,
    Guid MailboxId,
    Guid UserId,
    string Role);

public sealed record CreateSharedThreadRequest(
    Guid MailboxId,
    string Subject,
    string CustomerEmail,
    string? CustomerName = null,
    string Body = "",
    string Priority = "Medium",
    Guid? LeadId = null,
    Guid? ContactId = null,
    Guid? AccountId = null,
    Guid? DealId = null);

public sealed record SharedThreadDto(
    Guid Id,
    Guid MailboxId,
    string Subject,
    string CustomerEmail,
    string? CustomerName,
    string Status,
    string Priority,
    Guid? AssignedUserId,
    DateTime? AssignedAtUtc,
    DateTime LastMessageAtUtc,
    DateTime? FirstResponseAtUtc,
    DateTime? ClosedAtUtc,
    Guid? LeadId,
    Guid? ContactId,
    Guid? AccountId,
    Guid? DealId,
    int MessageCount);

public sealed record AssignThreadRequest(
    Guid? AssignedUserId);

public sealed record UpdateThreadStatusRequest(
    string Status);

public sealed record PostThreadMessageRequest(
    string Body,
    bool IsInternalNote = false);

public sealed record SharedThreadMessageDto(
    Guid Id,
    Guid ThreadId,
    string SenderType,
    string SenderEmail,
    string? SenderName,
    Guid? SenderUserId,
    string Body,
    bool IsInternalNote,
    DateTime SentAtUtc);

public sealed record AcquireLockRequest(
    string Action = "Drafting");

public sealed record CollisionStatusDto(
    Guid ThreadId,
    bool HasCollision,
    Guid? LockedByUserId,
    string? LockedByUserName,
    string? Action,
    DateTime? ExpiresAtUtc);

public sealed record SharedThreadQuery(
    Guid? MailboxId = null,
    Guid? AssignedUserId = null,
    string? Status = null,
    string? Priority = null,
    bool? UnassignedOnly = null,
    int Limit = 50);
