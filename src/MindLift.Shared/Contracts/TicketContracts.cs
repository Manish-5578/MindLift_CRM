namespace MindLift.Shared.Contracts;

public static class TicketStatuses
{
    public const string Open = "open";
    public const string Pending = "pending";
    public const string Resolved = "resolved";
    public const string Closed = "closed";

    public static readonly IReadOnlyList<string> All = [Open, Pending, Resolved, Closed];

    public static bool IsFinished(string status) => status is Resolved or Closed;
}

public static class TicketPriorities
{
    public const string Low = "low";
    public const string Normal = "normal";
    public const string High = "high";
    public const string Urgent = "urgent";

    public static readonly IReadOnlyList<string> All = [Low, Normal, High, Urgent];

    /// <summary>How long a first answer may take, by priority.</summary>
    public static TimeSpan AnswerWithin(string priority) => priority switch
    {
        Urgent => TimeSpan.FromHours(4),
        High => TimeSpan.FromHours(8),
        Low => TimeSpan.FromHours(72),
        _ => TimeSpan.FromHours(24),
    };
}

/// <summary>Filters for the ticket list. Every part is optional.</summary>
public sealed record TicketQuery(
    string? Search = null,
    string? Status = null,
    string? Priority = null,
    Guid? AssigneeId = null,
    bool MineOnly = false,
    bool LateOnly = false,
    bool UnassignedOnly = false,
    int Page = 1,
    int PageSize = 25);

public sealed record TicketListItemDto(
    Guid Id,
    string Number,
    string Subject,
    string Status,
    string Priority,
    string? ContactName,
    string? AccountName,
    Guid? AssigneeId,
    string? AssigneeName,
    DateTime? DueAt,
    bool IsLate,
    DateTime LastActivityAt,
    DateTime CreatedAt);

public sealed record TicketCommentDto(Guid Id, string AuthorName, string Body, bool IsInternal, DateTime CreatedAt);

public sealed record TicketDto(
    Guid Id,
    string Number,
    string Subject,
    string? Description,
    string Status,
    string Priority,
    string? Category,
    string Channel,
    Guid? ContactId,
    string? ContactName,
    string? ContactEmail,
    Guid? AccountId,
    string? AccountName,
    Guid? AssigneeId,
    string? AssigneeName,
    DateTime? DueAt,
    bool IsLate,
    DateTime? FirstResponseAt,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    DateTime LastActivityAt,
    IReadOnlyList<TicketCommentDto> Comments);

public sealed record SaveTicketRequest(
    string Subject,
    string? Description,
    string? Priority,
    string? Category,
    Guid? ContactId,
    Guid? AccountId,
    Guid? AssigneeId);

public sealed record ChangeTicketStatusRequest(string Status);

public sealed record AssignTicketRequest(Guid? AssigneeId);

/// <summary>A reply. <c>EmailCustomer</c> also emails a public reply to the ticket's contact.</summary>
public sealed record AddTicketCommentRequest(string Body, bool IsInternal = false, bool EmailCustomer = false);

/// <summary>How the help desk is doing: open work, what is late, and how fast it is answered.</summary>
public sealed record TicketSummaryDto(int Open, int Pending, int Late, int Unassigned, int ResolvedLast7Days, double? AverageFirstResponseHours, IReadOnlyList<PlatformSlice> ByPriority);
