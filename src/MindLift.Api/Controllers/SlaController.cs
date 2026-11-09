using MindLift.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 4 / #24: First-response SLA and escalation (response-sla).
/// Endpoints for managing SLA policies, tracking first response times, and
/// executing breach escalation checks.
/// </summary>
[ApiController]
[Authorize]
[RequiresFeature("response-sla")]
[Route("api/v1/sla")]
public sealed class SlaController(ISlaService slaService) : ControllerBase
{
    // ================= Policies =================

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost("policies")]
    public async Task<ActionResult<SlaPolicyDto>> CreatePolicy(
        [FromBody] SaveSlaPolicyRequest request,
        CancellationToken ct)
    {
        var created = await slaService.CreatePolicyAsync(request, ct);
        return CreatedAtAction(nameof(GetPolicyById), new { id = created.Id }, created);
    }

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("policies")]
    public async Task<ActionResult<IReadOnlyList<SlaPolicyDto>>> ListPolicies(
        [FromQuery] bool? activeOnly,
        CancellationToken ct)
    {
        var list = await slaService.ListPoliciesAsync(activeOnly, ct);
        return Ok(list);
    }

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("policies/{id:guid}")]
    public async Task<ActionResult<SlaPolicyDto>> GetPolicyById(
        Guid id,
        CancellationToken ct)
    {
        var policy = await slaService.GetPolicyByIdAsync(id, ct);
        return Ok(policy);
    }

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("policies/{id:guid}")]
    public async Task<ActionResult<SlaPolicyDto>> UpdatePolicy(
        Guid id,
        [FromBody] SaveSlaPolicyRequest request,
        CancellationToken ct)
    {
        var updated = await slaService.UpdatePolicyAsync(id, request, ct);
        return Ok(updated);
    }

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("policies/{id:guid}")]
    public async Task<IActionResult> DeletePolicy(
        Guid id,
        CancellationToken ct)
    {
        var deleted = await slaService.DeletePolicyAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    // ================= Lead SLA Tracking =================

    [HasPermission(AppModule.Leads, PermissionAction.Edit)]
    [HttpPost("leads/{leadId:guid}/attach")]
    public async Task<ActionResult<LeadSlaTrackerDto>> Attach(
        Guid leadId,
        CancellationToken ct)
    {
        var tracker = await slaService.AttachSlaToLeadAsync(leadId, ct);
        if (tracker == null) return NotFound("No matching active SLA policy found for lead.");
        return Ok(tracker);
    }

    [HasPermission(AppModule.Leads, PermissionAction.Edit)]
    [HttpPost("leads/{leadId:guid}/respond")]
    public async Task<ActionResult<LeadSlaTrackerDto>> RecordResponse(
        Guid leadId,
        [FromBody] RecordResponseRequest? request,
        CancellationToken ct)
    {
        var tracker = await slaService.RecordFirstResponseAsync(leadId, request, ct);
        return Ok(tracker);
    }

    [HasPermission(AppModule.Leads, PermissionAction.View)]
    [HttpGet("leads/{leadId:guid}")]
    public async Task<ActionResult<LeadSlaTrackerDto>> GetSlaForLead(
        Guid leadId,
        CancellationToken ct)
    {
        var tracker = await slaService.GetSlaForLeadAsync(leadId, ct);
        if (tracker == null) return NotFound();
        return Ok(tracker);
    }

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost("process-escalations")]
    public async Task<ActionResult<SlaCheckResultDto>> ProcessEscalations(CancellationToken ct)
    {
        var result = await slaService.ProcessSlaEscalationsAsync(ct);
        return Ok(result);
    }

    [HasPermission(AppModule.Leads, PermissionAction.View)]
    [HttpGet("trackers")]
    public async Task<ActionResult<IReadOnlyList<LeadSlaTrackerDto>>> ListTrackers(
        [FromQuery] string? status,
        [FromQuery] bool? escalated,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var list = await slaService.ListTrackersAsync(status, escalated, limit, ct);
        return Ok(list);
    }
}
