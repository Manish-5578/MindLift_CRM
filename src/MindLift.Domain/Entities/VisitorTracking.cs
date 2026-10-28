using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 4 / #25: Website visitor tracking (visitor-tracking).
/// Configuration for client-side JavaScript tracking script attached to a web property.
/// </summary>
[Audited]
public sealed class WebsiteTracker : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Unique public token embedded in the website tracker snippet.
    /// </summary>
    public string TrackingToken { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Automatically create an inbound Lead when an unidentified visitor provides an email address.
    /// </summary>
    public bool AutoCreateLeads { get; set; }

    public string[] AllowedOrigins { get; set; } = [];
    public int TotalVisits { get; set; }
    public int TotalPageViews { get; set; }
}

/// <summary>
/// A browsing session belonging to a visitor.
/// </summary>
[Audited]
public sealed class VisitorSession : TenantEntity
{
    public Guid TrackerId { get; set; }
    public string VisitorId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;

    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public string? IdentifiedEmail { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public int PageViewsCount { get; set; }
    public int TotalDurationSeconds { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? InitialReferrer { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }

    public ICollection<VisitorPageView> PageViews { get; set; } = new List<VisitorPageView>();
}

/// <summary>
/// An individual page view event recorded within a visitor session.
/// </summary>
[Audited]
public sealed class VisitorPageView : TenantEntity
{
    public Guid SessionId { get; set; }
    public VisitorSession Session { get; set; } = null!;

    public string VisitorId { get; set; } = string.Empty;
    public Guid? LeadId { get; set; }

    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Referrer { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}
