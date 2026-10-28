namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 6 / #41: Velocity and funnel reports (velocity-reports).
/// Contracts for pipeline funnel stage metrics and sales velocity equation reporting.
/// </summary>
public sealed record PipelineFunnelReportRequest(
    Guid? PipelineId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    Guid? OwnerId = null);

public sealed record PipelineFunnelReportDto(
    Guid PipelineId,
    string PipelineName,
    int TotalDealsEntered,
    int TotalDealsWon,
    int TotalDealsLost,
    decimal OverallWinRate,
    decimal OverallConversionRate,
    decimal TotalPipelineValue,
    decimal WonValue,
    IReadOnlyList<FunnelStageMetricDto> Stages);

public sealed record FunnelStageMetricDto(
    Guid StageId,
    string StageName,
    int SortOrder,
    string StageType,
    int DealsCount,
    decimal DealsValue,
    decimal ConversionRateToNext,
    double AverageDurationDays,
    int DroppedCount);

public sealed record SalesVelocityReportRequest(
    Guid? PipelineId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    Guid? OwnerId = null);

public sealed record SalesVelocityReportDto(
    decimal SalesVelocityPerDay,
    int NumberOfOpportunities,
    decimal WinRatePercentage,
    decimal AverageDealSize,
    double SalesCycleLengthDays,
    string Currency,
    IReadOnlyList<OwnerVelocityDto> RepVelocities);

public sealed record OwnerVelocityDto(
    Guid OwnerId,
    string OwnerName,
    decimal SalesVelocityPerDay,
    int OpportunitiesCount,
    decimal WinRatePercentage,
    decimal AverageDealSize,
    double AverageCycleDays);
