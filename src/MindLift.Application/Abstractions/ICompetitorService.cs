using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 3 / #17: Competitor Tracking (competitors).
/// Service abstraction for managing competitor battlecards, deal associations, and win/loss analytics.
/// </summary>
public interface ICompetitorService
{
    Task<IReadOnlyList<CompetitorDto>> ListCompetitorsAsync(bool activeOnly = false, CancellationToken cancellationToken = default);

    Task<CompetitorDto> GetCompetitorByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CompetitorDto> CreateCompetitorAsync(SaveCompetitorRequest request, CancellationToken cancellationToken = default);

    Task<CompetitorDto> UpdateCompetitorAsync(Guid id, SaveCompetitorRequest request, CancellationToken cancellationToken = default);

    Task DeleteCompetitorAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DealCompetitorDto>> GetDealCompetitorsAsync(Guid dealId, CancellationToken cancellationToken = default);

    Task<DealCompetitorDto> AddCompetitorToDealAsync(Guid dealId, AddDealCompetitorRequest request, CancellationToken cancellationToken = default);

    Task<DealCompetitorDto> UpdateDealCompetitorAsync(Guid dealId, Guid competitorId, UpdateDealCompetitorRequest request, CancellationToken cancellationToken = default);

    Task RemoveCompetitorFromDealAsync(Guid dealId, Guid competitorId, CancellationToken cancellationToken = default);

    Task<CompetitorAnalyticsSummaryDto> GetCompetitorAnalyticsAsync(CancellationToken cancellationToken = default);
}
