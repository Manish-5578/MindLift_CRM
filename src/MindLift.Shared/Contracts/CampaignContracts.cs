namespace MindLift.Shared.Contracts;

public static class CampaignStatuses
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Cancelled = "cancelled";
}

/// <summary>Who a campaign goes to. Leads can be narrowed by status, source and tag; contacts by type.</summary>
public sealed record CampaignAudience(string Type, string? Status, string? Source, string? Tag, string? ContactType);

public sealed record SaveCampaignRequest(string Name, string Subject, string Body, CampaignAudience Audience);

public sealed record CampaignStatsDto(int Recipients, int Sent, int Failed, int Skipped, int Pending, int Opened, int Clicked, int Unsubscribed);

public sealed record CampaignListItemDto(Guid Id, string Name, string Subject, string Status, string AudienceType, DateTime? ScheduledAt, DateTime? StartedAt, DateTime? CompletedAt, DateTime CreatedAt, CampaignStatsDto Stats);

public sealed record CampaignDto(
    Guid Id,
    string Name,
    string Subject,
    string Body,
    string Status,
    CampaignAudience Audience,
    DateTime? ScheduledAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime CreatedAt,
    CampaignStatsDto Stats);

public sealed record CampaignRecipientDto(Guid Id, string Email, string Name, string Status, string? Reason, DateTime? SentAt, DateTime? OpenedAt, DateTime? ClickedAt, DateTime? UnsubscribedAt);

/// <summary>How many people a campaign would go to now, and how many of them are left out and why.</summary>
public sealed record AudiencePreviewDto(int Total, int Reachable, int NoEmail, int DoNotEmail, IReadOnlyList<string> Examples);

public sealed record ScheduleCampaignRequest(DateTime? At);

public sealed record SendTestRequest(string To);
