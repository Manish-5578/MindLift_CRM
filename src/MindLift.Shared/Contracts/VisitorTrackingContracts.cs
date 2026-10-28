namespace MindLift.Shared.Contracts;

public sealed class SaveWebsiteTrackerRequest
{
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public bool AutoCreateLeads { get; set; }
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class WebsiteTrackerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string TrackingToken { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool AutoCreateLeads { get; set; }
    public string[] AllowedOrigins { get; set; } = [];
    public int TotalVisits { get; set; }
    public int TotalPageViews { get; set; }
    public string EmbedSnippet { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class TrackHitRequest
{
    public string TrackingToken { get; set; } = string.Empty;
    public string VisitorId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Referrer { get; set; }
    public int DurationSeconds { get; set; }
    public string? IdentifiedEmail { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
}

public sealed class TrackHitResultDto
{
    public bool Success { get; set; }
    public string VisitorId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public bool Identified { get; set; }
    public Guid? AssociatedLeadId { get; set; }
    public Guid? AssociatedContactId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class IdentifyVisitorRequest
{
    public string TrackingToken { get; set; } = string.Empty;
    public string VisitorId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? CompanyName { get; set; }
    public string? Phone { get; set; }
}

public sealed class VisitorPageViewDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Referrer { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime ViewedAt { get; set; }
}

public sealed class VisitorSessionDto
{
    public Guid Id { get; set; }
    public Guid TrackerId { get; set; }
    public string VisitorId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public string? IdentifiedEmail { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public int PageViewsCount { get; set; }
    public int TotalDurationSeconds { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public List<VisitorPageViewDto> PageViews { get; set; } = [];
}

public sealed class TopPageDto
{
    public string Url { get; set; } = string.Empty;
    public int ViewsCount { get; set; }
}

public sealed class VisitorAnalyticsDto
{
    public int TotalVisitors { get; set; }
    public int ActiveNowCount { get; set; }
    public int TotalPageViews { get; set; }
    public double AverageDurationSeconds { get; set; }
    public List<TopPageDto> TopPages { get; set; } = [];
}
