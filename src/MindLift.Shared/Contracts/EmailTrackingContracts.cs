using System.ComponentModel.DataAnnotations;

namespace MindLift.Shared.Contracts;

public sealed class TrackedEmailDto
{
    public Guid Id { get; set; }
    public string TrackingToken { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? SenderEmail { get; set; }
    public Guid? SentByUserId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? MessageId { get; set; }
    public DateTime SentAtUtc { get; set; }
    public int OpenCount { get; set; }
    public int UniqueOpenCount { get; set; }
    public DateTime? FirstOpenedAtUtc { get; set; }
    public DateTime? LastOpenedAtUtc { get; set; }
    public int ClickCount { get; set; }
    public int UniqueClickCount { get; set; }
    public DateTime? FirstClickedAtUtc { get; set; }
    public DateTime? LastClickedAtUtc { get; set; }
    public bool IsBounced { get; set; }
    public string? BounceReason { get; set; }
}

public sealed class RegisterTrackedEmailRequest
{
    [Required, EmailAddress]
    public string RecipientEmail { get; set; } = string.Empty;
    [Required]
    public string Subject { get; set; } = string.Empty;
    public string? SenderEmail { get; set; }
    [Required]
    public string HtmlBody { get; set; } = string.Empty;
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? MessageId { get; set; }
}

public sealed class RegisterTrackedEmailResult
{
    public Guid TrackedEmailId { get; set; }
    public string TrackingToken { get; set; } = string.Empty;
    public string ProcessedHtmlBody { get; set; } = string.Empty;
    public int LinksRewrittenCount { get; set; }
}

public sealed class TrackedEmailEventDto
{
    public Guid Id { get; set; }
    public Guid TrackedEmailId { get; set; }
    public string EventType { get; set; } = "Open";
    public string? Url { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceType { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class EmailTrackingSettingsDto
{
    public bool TrackOpens { get; set; } = true;
    public bool TrackClicks { get; set; } = true;
    public bool NotifyOnFirstOpen { get; set; } = true;
    public bool NotifyOnLinkClick { get; set; } = true;
    public string? CustomTrackingDomain { get; set; }
}

public sealed class SaveEmailTrackingSettingsRequest
{
    public bool TrackOpens { get; set; } = true;
    public bool TrackClicks { get; set; } = true;
    public bool NotifyOnFirstOpen { get; set; } = true;
    public bool NotifyOnLinkClick { get; set; } = true;
    public string? CustomTrackingDomain { get; set; }
}

public sealed class EmailTrackingStatsDto
{
    public int TotalSent { get; set; }
    public int TotalOpens { get; set; }
    public int UniqueOpens { get; set; }
    public double OpenRatePercent { get; set; }
    public int TotalClicks { get; set; }
    public int UniqueClicks { get; set; }
    public double ClickRatePercent { get; set; }
}
