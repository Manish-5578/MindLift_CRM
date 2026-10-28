using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 3 / #18: Win/Loss Analysis (win-loss).
/// Service abstraction for analyzing win/loss reasons, competitor correlations, sales cycle duration, and pipeline leakage.
/// </summary>
public interface IWinLossAnalysisService
{
    Task<WinLossSummaryDto> GetSummaryAsync(WinLossFilterQuery query, CancellationToken cancellationToken = default);

    Task<WinLossReasonsBreakdownDto> GetReasonsBreakdownAsync(WinLossFilterQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompetitorWinLossDto>> GetCompetitorCorrelationAsync(WinLossFilterQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StageLeakageDto>> GetPipelineLeakageAsync(WinLossFilterQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepWinLossDto>> GetSalesRepPerformanceAsync(WinLossFilterQuery query, CancellationToken cancellationToken = default);
}
