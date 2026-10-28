namespace MindLift.Shared.Contracts;

public sealed record CreateMeetingLinkRequest(
    string Title,
    string? Slug = null,
    string? Description = null,
    int DurationMinutes = 30,
    string LocationType = "Video",
    string? LocationValue = null,
    Guid? BookingPageId = null,
    Guid? LeadId = null,
    Guid? ContactId = null,
    Guid? DealId = null,
    string? CustomerName = null,
    string? CustomerEmail = null,
    string? CustomerPhone = null,
    bool IsSingleUse = false,
    DateTime? ExpiresAtUtc = null);

public sealed record UpdateMeetingLinkRequest(
    string Title,
    string? Description = null,
    int DurationMinutes = 30,
    string LocationType = "Video",
    string? LocationValue = null,
    bool IsActive = true,
    DateTime? ExpiresAtUtc = null);

public sealed record MeetingLinkDto(
    Guid Id,
    Guid UserId,
    string UserName,
    Guid? BookingPageId,
    string Slug,
    string Title,
    string? Description,
    int DurationMinutes,
    string LocationType,
    string? LocationValue,
    Guid? LeadId,
    Guid? ContactId,
    Guid? DealId,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    bool IsSingleUse,
    bool IsUsed,
    bool IsActive,
    int ClickCount,
    int BookingCount,
    decimal ConversionRate,
    DateTime? ExpiresAtUtc,
    string ShareableUrl);

public sealed record PublicMeetingLinkDto(
    string Slug,
    string Title,
    string? Description,
    int DurationMinutes,
    string LocationType,
    string HostName,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    bool IsExpired,
    bool IsAvailable,
    string BookingPageSlug,
    string WorkspaceSlug);

public sealed record BookThroughMeetingLinkRequest(
    DateTime StartAtUtc,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone = null,
    string? Notes = null);

public sealed record MeetingLinkQuery(
    Guid? UserId = null,
    Guid? DealId = null,
    Guid? ContactId = null,
    Guid? LeadId = null,
    bool? IsActive = null,
    int Limit = 50);
