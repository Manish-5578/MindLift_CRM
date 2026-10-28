using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 5 / #30: Email open and click tracking (email-tracking).
/// Tracks engagement metrics (opens, clicks, delivery) for outbound CRM emails.
/// </summary>
[Audited]
public sealed class TrackedEmail : TenantEntity
{
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

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

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

    public ICollection<TrackedEmailLink> Links { get; set; } = [];
    public ICollection<TrackedEmailEvent> Events { get; set; } = [];
}

/// <summary>
/// Individual URL link rewritten inside a tracked outbound email.
/// </summary>
[Audited]
public sealed class TrackedEmailLink : TenantEntity
{
    public Guid TrackedEmailId { get; set; }
    public TrackedEmail TrackedEmail { get; set; } = null!;
    public string LinkToken { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public int ClickCount { get; set; }
    public DateTime? FirstClickedAtUtc { get; set; }
    public DateTime? LastClickedAtUtc { get; set; }
}

/// <summary>
/// Granular event record for each open and link click occurrence.
/// </summary>
[Audited]
public sealed class TrackedEmailEvent : TenantEntity
{
    public Guid TrackedEmailId { get; set; }
    public TrackedEmail TrackedEmail { get; set; } = null!;
    public string EventType { get; set; } = "Open"; // "Open", "Click"
    public string? LinkToken { get; set; }
    public string? Url { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceType { get; set; } = "Desktop";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tenant-level configuration for email engagement tracking.
/// </summary>
[Audited]
public sealed class EmailTrackingSettings : TenantEntity
{
    public bool TrackOpens { get; set; } = true;
    public bool TrackClicks { get; set; } = true;
    public bool NotifyOnFirstOpen { get; set; } = true;
    public bool NotifyOnLinkClick { get; set; } = true;
    public string? CustomTrackingDomain { get; set; }
}
