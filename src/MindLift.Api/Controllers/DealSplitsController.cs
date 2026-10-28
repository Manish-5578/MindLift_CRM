using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 3 / #14: Deal Revenue Splits (deal-splits).
/// Endpoints for managing revenue and overlay credit splits among sales team members.
/// </summary>
[ApiController]
[Route("api/v1/deals")]
[RequiresFeature("deal-splits")]
[Authorize]
public sealed class DealSplitsController(IDealSplitService dealSplits) : ControllerBase
{
    [HttpGet("{dealId:guid}/splits")]
    public async Task<ActionResult<DealSplitSummaryDto>> GetSplits(
        Guid dealId,
        CancellationToken cancellationToken)
        => Ok(await dealSplits.GetDealSplitsAsync(dealId, cancellationToken));

    [HttpPut("{dealId:guid}/splits")]
    public async Task<ActionResult<DealSplitSummaryDto>> SetSplits(
        Guid dealId,
        [FromBody] SetDealSplitsRequest request,
        CancellationToken cancellationToken)
        => Ok(await dealSplits.SetDealSplitsAsync(dealId, request, cancellationToken));

    [HttpPost("{dealId:guid}/splits")]
    public async Task<ActionResult<DealSplitMemberDto>> AddOrUpdateSplit(
        Guid dealId,
        [FromBody] SaveDealSplitMemberRequest request,
        CancellationToken cancellationToken)
        => Ok(await dealSplits.AddOrUpdateSplitAsync(dealId, request, cancellationToken));

    [HttpDelete("{dealId:guid}/splits/{splitId:guid}")]
    public async Task<ActionResult<DealSplitSummaryDto>> RemoveSplit(
        Guid dealId,
        Guid splitId,
        CancellationToken cancellationToken)
        => Ok(await dealSplits.RemoveSplitAsync(dealId, splitId, cancellationToken));

    [HttpGet("splits/users/{userId:guid}")]
    public async Task<ActionResult<UserDealSplitsSummaryDto>> GetUserSplits(
        Guid userId,
        CancellationToken cancellationToken)
        => Ok(await dealSplits.GetUserSplitsAsync(userId, cancellationToken));
}
