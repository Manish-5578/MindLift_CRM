using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 6 / #42: Leaderboards (leaderboards).
/// Gamified sales rep rankings, podium medals, and quota attainment tiers.
/// </summary>
[ApiController]
[Route("api/v1/leaderboards")]
[Authorize]
[RequiresFeature("leaderboards")]
public sealed class LeaderboardsController(ILeaderboardService leaderboardService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LeaderboardResponseDto>> Get(
        [FromQuery] LeaderboardQueryRequest request,
        CancellationToken ct)
    {
        var leaderboard = await leaderboardService.GetLeaderboardAsync(request, ct);
        return Ok(leaderboard);
    }

    [HttpGet("meta")]
    public async Task<ActionResult<LeaderboardMetaDto>> GetMeta(CancellationToken ct)
    {
        var meta = await leaderboardService.GetMetaAsync(ct);
        return Ok(meta);
    }
}
