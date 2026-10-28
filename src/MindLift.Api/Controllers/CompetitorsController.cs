using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 3 / #17: Competitor Tracking (competitors).
/// Endpoints for managing competitor profiles, battlecards, deal associations, and win/loss analytics.
/// </summary>
[ApiController]
[RequiresFeature("competitors")]
[Authorize]
public sealed class CompetitorsController(ICompetitorService competitors) : ControllerBase
{
    [HttpGet("api/v1/competitors")]
    public async Task<ActionResult<IReadOnlyList<CompetitorDto>>> List(
        [FromQuery] bool activeOnly,
        CancellationToken cancellationToken)
        => Ok(await competitors.ListCompetitorsAsync(activeOnly, cancellationToken));

    [HttpGet("api/v1/competitors/analytics")]
    public async Task<ActionResult<CompetitorAnalyticsSummaryDto>> GetAnalytics(
        CancellationToken cancellationToken)
        => Ok(await competitors.GetCompetitorAnalyticsAsync(cancellationToken));

    [HttpGet("api/v1/competitors/{id:guid}")]
    public async Task<ActionResult<CompetitorDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await competitors.GetCompetitorByIdAsync(id, cancellationToken));

    [HttpPost("api/v1/competitors")]
    public async Task<ActionResult<CompetitorDto>> Create(
        [FromBody] SaveCompetitorRequest request,
        CancellationToken cancellationToken)
        => Ok(await competitors.CreateCompetitorAsync(request, cancellationToken));

    [HttpPut("api/v1/competitors/{id:guid}")]
    public async Task<ActionResult<CompetitorDto>> Update(
        Guid id,
        [FromBody] SaveCompetitorRequest request,
        CancellationToken cancellationToken)
        => Ok(await competitors.UpdateCompetitorAsync(id, request, cancellationToken));

    [HttpDelete("api/v1/competitors/{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await competitors.DeleteCompetitorAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("api/v1/deals/{dealId:guid}/competitors")]
    public async Task<ActionResult<IReadOnlyList<DealCompetitorDto>>> GetDealCompetitors(
        Guid dealId,
        CancellationToken cancellationToken)
        => Ok(await competitors.GetDealCompetitorsAsync(dealId, cancellationToken));

    [HttpPost("api/v1/deals/{dealId:guid}/competitors")]
    public async Task<ActionResult<DealCompetitorDto>> AddCompetitorToDeal(
        Guid dealId,
        [FromBody] AddDealCompetitorRequest request,
        CancellationToken cancellationToken)
        => Ok(await competitors.AddCompetitorToDealAsync(dealId, request, cancellationToken));

    [HttpPut("api/v1/deals/{dealId:guid}/competitors/{competitorId:guid}")]
    public async Task<ActionResult<DealCompetitorDto>> UpdateDealCompetitor(
        Guid dealId,
        Guid competitorId,
        [FromBody] UpdateDealCompetitorRequest request,
        CancellationToken cancellationToken)
        => Ok(await competitors.UpdateDealCompetitorAsync(dealId, competitorId, request, cancellationToken));

    [HttpDelete("api/v1/deals/{dealId:guid}/competitors/{competitorId:guid}")]
    public async Task<IActionResult> RemoveCompetitorFromDeal(
        Guid dealId,
        Guid competitorId,
        CancellationToken cancellationToken)
    {
        await competitors.RemoveCompetitorFromDealAsync(dealId, competitorId, cancellationToken);
        return NoContent();
    }
}
