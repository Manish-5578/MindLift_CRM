using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 4 / #22: Round-robin and territory routing (lead-routing).
/// Endpoints for territory definition, member management, lead routing distribution,
/// routing simulation, and audit logging.
/// </summary>
[ApiController]
[Authorize]
[RequiresFeature("lead-routing")]
[Route("api/v1/territories")]
public sealed class TerritoriesController(IRoutingService routingService) : ControllerBase
{
    // ================= Territories =================

    [HttpPost]
    public async Task<ActionResult<TerritoryDto>> Create(
        [FromBody] SaveTerritoryRequest request,
        CancellationToken ct)
    {
        var created = await routingService.CreateTerritoryAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TerritoryDto>>> List(
        [FromQuery] bool? activeOnly,
        CancellationToken ct)
    {
        var list = await routingService.ListTerritoriesAsync(activeOnly, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TerritoryDto>> GetById(
        Guid id,
        CancellationToken ct)
    {
        var territory = await routingService.GetTerritoryByIdAsync(id, ct);
        return Ok(territory);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TerritoryDto>> Update(
        Guid id,
        [FromBody] SaveTerritoryRequest request,
        CancellationToken ct)
    {
        var updated = await routingService.UpdateTerritoryAsync(id, request, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken ct)
    {
        var deleted = await routingService.DeleteTerritoryAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    // ================= Members =================

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<TerritoryMemberDto>>> ListMembers(
        Guid id,
        CancellationToken ct)
    {
        var members = await routingService.ListMembersAsync(id, ct);
        return Ok(members);
    }

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<TerritoryMemberDto>> AddOrUpdateMember(
        Guid id,
        [FromBody] SaveTerritoryMemberRequest request,
        CancellationToken ct)
    {
        var member = await routingService.AddOrUpdateMemberAsync(id, request, ct);
        return Ok(member);
    }

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid id,
        Guid userId,
        CancellationToken ct)
    {
        var removed = await routingService.RemoveMemberAsync(id, userId, ct);
        if (!removed) return NotFound();
        return NoContent();
    }

    // ================= Lead Routing & Simulation =================

    [HttpPost("route-lead/{leadId:guid}")]
    [HttpPost("/api/v1/routing/route-lead/{leadId:guid}")]
    public async Task<ActionResult<RouteLeadResultDto>> RouteLead(
        Guid leadId,
        [FromBody] RouteLeadRequest? request,
        CancellationToken ct)
    {
        var result = await routingService.RouteLeadAsync(leadId, request, ct);
        return Ok(result);
    }

    [HttpPost("simulate")]
    [HttpPost("/api/v1/routing/simulate")]
    public async Task<ActionResult<SimulateRoutingResultDto>> Simulate(
        [FromBody] SimulateRoutingRequest request,
        CancellationToken ct)
    {
        var result = await routingService.SimulateRoutingAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("logs")]
    [HttpGet("/api/v1/routing/logs")]
    public async Task<ActionResult<IReadOnlyList<LeadRoutingLogDto>>> ListLogs(
        [FromQuery] Guid? leadId,
        [FromQuery] Guid? territoryId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var logs = await routingService.ListRoutingLogsAsync(leadId, territoryId, limit, ct);
        return Ok(logs);
    }
}
