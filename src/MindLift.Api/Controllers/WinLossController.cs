using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 3 / #18: Win/Loss Analysis (win-loss).
/// Endpoints for win/loss reason analysis, competitor correlation, sales cycle durations, and pipeline leakage.
/// </summary>
[ApiController]
[Route("api/v1/win-loss")]
[RequiresFeature("win-loss")]
[Authorize]
public sealed class WinLossController(IWinLossAnalysisService winLoss) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<WinLossSummaryDto>> GetSummary(
        [FromQuery] WinLossFilterQuery query,
        CancellationToken cancellationToken)
        => Ok(await winLoss.GetSummaryAsync(query, cancellationToken));

    [HttpGet("reasons")]
    public async Task<ActionResult<WinLossReasonsBreakdownDto>> GetReasons(
        [FromQuery] WinLossFilterQuery query,
        CancellationToken cancellationToken)
        => Ok(await winLoss.GetReasonsBreakdownAsync(query, cancellationToken));

    [HttpGet("competitors")]
    public async Task<ActionResult<IReadOnlyList<CompetitorWinLossDto>>> GetCompetitorCorrelation(
        [FromQuery] WinLossFilterQuery query,
        CancellationToken cancellationToken)
        => Ok(await winLoss.GetCompetitorCorrelationAsync(query, cancellationToken));

    [HttpGet("stages")]
    public async Task<ActionResult<IReadOnlyList<StageLeakageDto>>> GetPipelineLeakage(
        [FromQuery] WinLossFilterQuery query,
        CancellationToken cancellationToken)
        => Ok(await winLoss.GetPipelineLeakageAsync(query, cancellationToken));

    [HttpGet("reps")]
    public async Task<ActionResult<IReadOnlyList<RepWinLossDto>>> GetSalesRepPerformance(
        [FromQuery] WinLossFilterQuery query,
        CancellationToken cancellationToken)
        => Ok(await winLoss.GetSalesRepPerformanceAsync(query, cancellationToken));
}
