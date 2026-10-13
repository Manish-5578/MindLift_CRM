namespace MindLift.Shared.Contracts;

/// <summary>The kinds of notification, with their labels and the defaults a new user starts with (plan 7.11).</summary>
public static class NotificationTypes
{
    public const string LeadAssigned = "LeadAssigned";
    public const string DealAssigned = "DealAssigned";
    public const string TaskAssigned = "TaskAssigned";
    public const string AccountAssigned = "AccountAssigned";
    public const string ContactAssigned = "ContactAssigned";
    public const string Mentioned = "Mentioned";
    public const string TaskDueSoon = "TaskDueSoon";
    public const string TaskOverdue = "TaskOverdue";
    public const string MeetingReminder = "MeetingReminder";
    public const string DealWon = "DealWon";
    public const string DealLost = "DealLost";
    public const string BookingCreated = "BookingCreated";
    public const string BookingCancelled = "BookingCancelled";
    public const string ImportCompleted = "ImportCompleted";
    public const string TrialEnding = "TrialEnding";
    public const string PortalUpload = "PortalUpload";

    /// <summary>A preference row, not a notification: the optional "my day" email sent each morning.</summary>
    public const string DailyDigest = "DailyDigest";

    /// <summary>Every type with its label and whether email is on by default. In-app is on for all.</summary>
    public static readonly IReadOnlyList<(string Type, string Label, bool EmailByDefault)> All =
    [
        (LeadAssigned, "A lead is assigned to me", true),
        (DealAssigned, "A deal is assigned to me", true),
        (AccountAssigned, "An account is assigned to me", true),
        (ContactAssigned, "A contact is assigned to me", true),
        (TaskAssigned, "A task is assigned to me", true),
        (Mentioned, "Someone mentions me in a note", true),
        (TaskDueSoon, "A task is about to be due", false),
        (TaskOverdue, "A task of mine is overdue", true),
        (MeetingReminder, "A meeting is about to start", true),
        (DealWon, "A deal is won", false),
        (DealLost, "A deal is lost", false),
        (BookingCreated, "A customer books a time with me", true),
        (BookingCancelled, "A customer cancels a booking", true),
        (ImportCompleted, "An import finishes", true),
        (TrialEnding, "My trial is ending", true),
        (PortalUpload, "A customer uploads a requested document", true),
    ];

    public static bool IsKnown(string type) => type == DailyDigest || All.Any(t => t.Type == type);
}

public sealed record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string? Body,
    string? EntityType,
    Guid? EntityId,
    string? Url,
    bool IsRead,
    DateTime CreatedAtUtc);

public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, int Total, int Unread, int Page, int PageSize);

public sealed record UnreadCountDto(int Unread);

public sealed record NotificationPreferenceDto(string Type, string Label, bool InApp, bool Email);

public sealed record SaveNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceDto> Preferences, bool DailyDigest);

public sealed record NotificationPreferencesDto(IReadOnlyList<NotificationPreferenceDto> Preferences, bool DailyDigest);
