namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 3 / #18: Win/Loss Analysis (win-loss).
/// Contracts for win/loss reasons, competitor correlations, sales cycle durations, and pipeline leakage analytics.
/// </summary>
public sealed record WinLossFilterQuery(
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    Guid? PipelineId = null,
    Guid? OwnerId = null);

public sealed record WinLossReasonStatDto(
    string Reason,
    int DealCount,
    decimal PercentageOfTotal,
    decimal TotalRevenue);

public sealed record WinLossReasonsBreakdownDto(
    IReadOnlyList<WinLossReasonStatDto> WinReasons,
    IReadOnlyList<WinLossReasonStatDto> LossReasons);

public sealed record CompetitorWinLossDto(
    Guid CompetitorId,
    string CompetitorName,
    int EncountersCount,
    int WonCount,
    int LostCount,
    decimal WinRatePercentage,
    decimal WonRevenue,
    decimal LostRevenue);

public sealed record StageLeakageDto(
    Guid StageId,
    string StageName,
    int StageOrder,
    int LostCount,
    decimal LostRevenue,
    decimal LeakagePercentage);

public sealed record RepWinLossDto(
    Guid OwnerId,
    string OwnerName,
    int TotalDeals,
    int WonDeals,
    int LostDeals,
    decimal WinRatePercentage,
    decimal WonRevenue,
    decimal LostRevenue,
    decimal AverageDaysToWin,
    decimal AverageDaysToLose);

public sealed record WinLossSummaryDto(
    int TotalClosedDeals,
    int WonDealsCount,
    int LostDealsCount,
    decimal OverallWinRatePercentage,
    decimal WonRevenue,
    decimal LostRevenue,
    decimal AverageWonDealSize,
    decimal AverageLostDealSize,
    decimal AverageDaysToWin,
    decimal AverageDaysToLose,
    WinLossReasonsBreakdownDto Reasons,
    IReadOnlyList<CompetitorWinLossDto> Competitors,
    IReadOnlyList<StageLeakageDto> PipelineLeakage,
    IReadOnlyList<RepWinLossDto> SalesReps);
