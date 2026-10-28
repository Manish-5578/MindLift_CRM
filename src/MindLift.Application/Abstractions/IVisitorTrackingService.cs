using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IVisitorTrackingService
{
    // Tracker Management
    Task<WebsiteTrackerDto> CreateTrackerAsync(SaveWebsiteTrackerRequest req, CancellationToken ct = default);
    Task<WebsiteTrackerDto> GetTrackerByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WebsiteTrackerDto>> ListTrackersAsync(CancellationToken ct = default);
    Task<WebsiteTrackerDto> UpdateTrackerAsync(Guid id, SaveWebsiteTrackerRequest req, CancellationToken ct = default);
    Task<bool> DeleteTrackerAsync(Guid id, CancellationToken ct = default);

    // Ingestion & Visitor Journey
    Task<TrackHitResultDto> TrackHitAsync(TrackHitRequest req, CancellationToken ct = default);
    Task<TrackHitResultDto> IdentifyVisitorAsync(IdentifyVisitorRequest req, CancellationToken ct = default);

    // Insights & Sessions
    Task<IReadOnlyList<VisitorSessionDto>> GetVisitorSessionsAsync(Guid? trackerId = null, Guid? leadId = null, int limit = 50, CancellationToken ct = default);
    Task<VisitorAnalyticsDto> GetAnalyticsSummaryAsync(Guid? trackerId = null, CancellationToken ct = default);
}
