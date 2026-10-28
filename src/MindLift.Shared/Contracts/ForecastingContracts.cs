namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 3 / #15: Forecasting and Weighted Pipeline (forecasting).
/// Contracts for pipeline weighting, forecast categories, quota attainment, and sales projections.
/// </summary>
public static class ForecastCategories
{
    public const string Pipeline = "Pipeline";
    public const string BestCase = "BestCase";
    public const string Commit = "Commit";
    public const string Closed = "Closed";
    public const string Omitted = "Omitted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Pipeline, BestCase, Commit, Closed, Omitted
    };
}

public sealed record ForecastCategorySummaryDto(
    string Category,
    int DealCount,
    decimal TotalAmount,
    decimal WeightedAmount);

public sealed record UserForecastAttainmentDto(
    Guid UserId,
    string UserName,
    string UserEmail,
    decimal QuotaTarget,
    decimal ClosedWonAmount,
    decimal CommitAmount,
    decimal BestCaseAmount,
    decimal PipelineAmount,
    decimal WeightedPipelineAmount,
    decimal ProjectedTotal,
    decimal AttainmentPercentage,
    decimal GapToTarget);

public sealed record ForecastSummaryDto(
    string Period,
    DateTime StartDate,
    DateTime EndDate,
    decimal TotalQuota,
    decimal ClosedWonAmount,
    decimal TotalPipelineAmount,
    decimal WeightedPipelineAmount,
    decimal CommitAmount,
    decimal BestCaseAmount,
    decimal ProjectedAttainmentAmount,
    decimal AttainmentPercentage,
    decimal GapToTarget,
    IReadOnlyList<ForecastCategorySummaryDto> ByCategory,
    IReadOnlyList<UserForecastAttainmentDto> ByUser);

public sealed record ForecastFilterQuery(
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    Guid? PipelineId = null,
    Guid? UserId = null);

public sealed record UpdateDealForecastCategoryRequest(
    string ForecastCategory);
