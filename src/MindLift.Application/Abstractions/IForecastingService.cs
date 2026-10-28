using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 3 / #15: Forecasting and Weighted Pipeline (forecasting).
/// Service abstraction for calculating weighted pipeline projections, forecast categories, and quota attainments.
/// </summary>
public interface IForecastingService
{
    Task<ForecastSummaryDto> GetForecastSummaryAsync(ForecastFilterQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserForecastAttainmentDto>> GetUserAttainmentsAsync(ForecastFilterQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ForecastCategorySummaryDto>> GetCategoryBreakdownsAsync(ForecastFilterQuery query, CancellationToken cancellationToken = default);

    Task<DealDto> UpdateDealForecastCategoryAsync(Guid dealId, UpdateDealForecastCategoryRequest request, CancellationToken cancellationToken = default);
}
