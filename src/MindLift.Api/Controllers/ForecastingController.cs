using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 3 / #15: Forecasting and Weighted Pipeline (forecasting).
/// Endpoints for pipeline weighting, forecast categories, quota attainment, and sales projections.
/// </summary>
[ApiController]
[Route("api/v1/forecasting")]
[RequiresFeature("forecasting")]
[Authorize]
public sealed class ForecastingController(IForecastingService forecasting) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<ForecastSummaryDto>> GetSummary(
        [FromQuery] ForecastFilterQuery query,
        CancellationToken cancellationToken)
        => Ok(await forecasting.GetForecastSummaryAsync(query, cancellationToken));

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<UserForecastAttainmentDto>>> GetUserAttainments(
        [FromQuery] ForecastFilterQuery query,
        CancellationToken cancellationToken)
        => Ok(await forecasting.GetUserAttainmentsAsync(query, cancellationToken));

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<ForecastCategorySummaryDto>>> GetCategories(
        [FromQuery] ForecastFilterQuery query,
        CancellationToken cancellationToken)
        => Ok(await forecasting.GetCategoryBreakdownsAsync(query, cancellationToken));

    [HttpPut("deals/{dealId:guid}/category")]
    public async Task<ActionResult<DealDto>> UpdateDealCategory(
        Guid dealId,
        [FromBody] UpdateDealForecastCategoryRequest request,
        CancellationToken cancellationToken)
        => Ok(await forecasting.UpdateDealForecastCategoryAsync(dealId, request, cancellationToken));
}
