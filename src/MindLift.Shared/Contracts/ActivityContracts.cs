namespace MindLift.Shared.Contracts;

/// <summary>The kinds of activity. Notes are plain text.</summary>
public static class ActivityTypes
{
    public const string Task = "Task";
    public const string Call = "Call";
    public const string Meeting = "Meeting";
    public const string Note = "Note";

    public static readonly IReadOnlyList<string> All = [Task, Call, Meeting, Note];
}

public sealed record ActivityDto(
    Guid Id,
    string Type,
    string Subject,
    string? Description,
    string Status,
    string Priority,
    DateTime? DueAtUtc,
    DateTime? EndAtUtc,
    int? DurationMinutes,
    string? Direction,
    string? CallOutcome,
    string? Location,
    int? ReminderMinutes,
    DateTime? CompletedAtUtc,
    Guid? LeadId,
    Guid? ContactId,
    Guid? AccountId,
    Guid? DealId,
    string? LinkedName,
    string? LinkedKind,
    Guid OwnerId,
    string? OwnerName,
    Guid? ParentActivityId,
    Guid? ChecklistTemplateId,
    bool IsOverdue,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> Attendees,
    IReadOnlyList<MentionDto> Mentions,
    DateTime? InvitationsSentAtUtc);

public sealed record MentionDto(Guid UserId, string Name);

public sealed record SaveActivityRequest(
    string Type,
    string Subject,
    string? Description,
    string? Priority,
    DateTime? DueAt,
    DateTime? EndAt,
    int? DurationMinutes,
    string? Direction,
    string? CallOutcome,
    string? Location,
    int? ReminderMinutes,
    Guid? LeadId,
    Guid? ContactId,
    Guid? AccountId,
    Guid? DealId,
    Guid? OwnerId,
    IReadOnlyList<string>? AttendeeEmails = null,
    bool SendInvitations = false,
    IReadOnlyList<Guid>? MentionUserIds = null);

public sealed record CompleteActivityRequest(string? Outcome, string? Notes, SaveActivityRequest? FollowUp);

/// <summary>Which activities to list. Tab is Today, Overdue, Upcoming, Completed or All.</summary>
public sealed record ActivityQuery(
    string? Tab,
    string? Type,
    Guid? OwnerId,
    bool MineOnly,
    Guid? LeadId,
    Guid? ContactId,
    Guid? AccountId,
    Guid? DealId,
    int Page = 1,
    int PageSize = 50);

public sealed record MyDayDto(int Overdue, int Today, int Upcoming, IReadOnlyList<ActivityDto> Items);

public sealed record ChecklistItemDto(string Subject, int OffsetDays, string? Description);

public sealed record ChecklistTemplateDto(Guid Id, string Name, string Trigger, Guid? PipelineId, bool IsActive, IReadOnlyList<ChecklistItemDto> Items);

public sealed record SaveChecklistTemplateRequest(string Name, Guid? PipelineId, bool IsActive, IReadOnlyList<ChecklistItemDto> Items);

public sealed record OnboardingProgressDto(Guid TemplateId, string TemplateName, int Total, int Completed, IReadOnlyList<ActivityDto> Tasks);

/// <summary>One line of a record's timeline. Kind is activity, created, changed, stage, converted, deal or contact.</summary>
public sealed record TimelineEntryDto(string Kind, DateTime WhenUtc, string Title, string? Detail, string? ActorName, string? Href, string? ActivityType);
