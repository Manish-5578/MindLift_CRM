using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 4 / #23: Lead scoring (lead-scoring).
/// Endpoints for managing scoring rules, evaluating individual and batch lead scores,
/// and inspecting score audit logs.
/// </summary>
[ApiController]
[Authorize]
[RequiresFeature("lead-scoring")]
[Route("api/v1/scoring")]
public sealed class LeadScoringController(ILeadScoringService scoringService) : ControllerBase
{
    // ================= Scoring Rules =================

    [HttpPost("rules")]
    public async Task<ActionResult<LeadScoringRuleDto>> CreateRule(
        [FromBody] SaveLeadScoringRuleRequest request,
        CancellationToken ct)
    {
        var created = await scoringService.CreateRuleAsync(request, ct);
        return CreatedAtAction(nameof(GetRuleById), new { id = created.Id }, created);
    }

    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<LeadScoringRuleDto>>> ListRules(
        [FromQuery] bool? activeOnly,
        CancellationToken ct)
    {
        var list = await scoringService.ListRulesAsync(activeOnly, ct);
        return Ok(list);
    }

    [HttpGet("rules/{id:guid}")]
    public async Task<ActionResult<LeadScoringRuleDto>> GetRuleById(
        Guid id,
        CancellationToken ct)
    {
        var rule = await scoringService.GetRuleByIdAsync(id, ct);
        return Ok(rule);
    }

    [HttpPut("rules/{id:guid}")]
    public async Task<ActionResult<LeadScoringRuleDto>> UpdateRule(
        Guid id,
        [FromBody] SaveLeadScoringRuleRequest request,
        CancellationToken ct)
    {
        var updated = await scoringService.UpdateRuleAsync(id, request, ct);
        return Ok(updated);
    }

    [HttpDelete("rules/{id:guid}")]
    public async Task<IActionResult> DeleteRule(
        Guid id,
        CancellationToken ct)
    {
        var deleted = await scoringService.DeleteRuleAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    // ================= Score Evaluation & Audit =================

    [HttpPost("evaluate/{leadId:guid}")]
    public async Task<ActionResult<LeadScoreResultDto>> Evaluate(
        Guid leadId,
        CancellationToken ct)
    {
        var result = await scoringService.EvaluateLeadScoreAsync(leadId, ct);
        return Ok(result);
    }

    [HttpPost("batch-evaluate")]
    public async Task<ActionResult<BatchScoreResultDto>> BatchEvaluate(
        [FromBody] BatchScoreRequest? request,
        CancellationToken ct)
    {
        var result = await scoringService.BatchEvaluateScoresAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("logs/{leadId:guid}")]
    public async Task<ActionResult<IReadOnlyList<LeadScoreLogDto>>> GetLogs(
        Guid leadId,
        CancellationToken ct)
    {
        var logs = await scoringService.GetScoreLogsAsync(leadId, ct);
        return Ok(logs);
    }
}
