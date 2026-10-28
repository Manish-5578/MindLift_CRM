using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 5 / #30: Email open and click tracking (email-tracking).
/// Injects open-tracking pixels, rewrites outbound HTML links for click tracking,
/// handles tracking pixel requests and click redirects, and logs engagement metrics.
/// </summary>
public interface IEmailTrackingService
{
    Task<RegisterTrackedEmailResult> RegisterAndTransformEmailAsync(RegisterTrackedEmailRequest request, string baseUrl, CancellationToken ct = default);
    Task<byte[]?> RecordOpenAsync(string trackingToken, string? ipAddress, string? userAgent, CancellationToken ct = default);
    Task<string?> RecordClickAsync(string linkToken, string? ipAddress, string? userAgent, CancellationToken ct = default);
    Task<TrackedEmailDto?> GetTrackedEmailAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TrackedEmailDto>> ListTrackedEmailsAsync(string? entityType, Guid? entityId, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<IReadOnlyList<TrackedEmailEventDto>> ListEventsAsync(Guid trackedEmailId, CancellationToken ct = default);
    Task<EmailTrackingSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<EmailTrackingSettingsDto> UpdateSettingsAsync(SaveEmailTrackingSettingsRequest request, CancellationToken ct = default);
    Task<EmailTrackingStatsDto> GetStatsAsync(CancellationToken ct = default);
}
