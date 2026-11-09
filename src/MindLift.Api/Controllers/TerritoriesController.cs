using MindLift.Shared.Enums;
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

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<TerritoryDto>> Create(
        [FromBody] SaveTerritoryRequest request,
        CancellationToken ct)
    {
        var created = await routingService.CreateTerritoryAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TerritoryDto>>> List(
        [FromQuery] bool? activeOnly,
        CancellationToken ct)
    {
        var list = await routingService.ListTerritoriesAsync(activeOnly, ct);
        return Ok(list);
    }

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TerritoryDto>> GetById(
        Guid id,
        CancellationToken ct)
    {
        var territory = await routingService.GetTerritoryByIdAsync(id, ct);
        return Ok(territory);
    }

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TerritoryDto>> Update(
        Guid id,
        [FromBody] SaveTerritoryRequest request,
        CancellationToken ct)
    {
        var updated = await routingService.UpdateTerritoryAsync(id, request, ct);
        return Ok(updated);
    }

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
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

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<TerritoryMemberDto>>> ListMembers(
        Guid id,
        CancellationToken ct)
    {
        var members = await routingService.ListMembersAsync(id, ct);
        return Ok(members);
    }

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<TerritoryMemberDto>> AddOrUpdateMember(
        Guid id,
        [FromBody] SaveTerritoryMemberRequest request,
        CancellationToken ct)
    {
        var member = await routingService.AddOrUpdateMemberAsync(id, request, ct);
        return Ok(member);
    }

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
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

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
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

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpPost("simulate")]
    [HttpPost("/api/v1/routing/simulate")]
    public async Task<ActionResult<SimulateRoutingResultDto>> Simulate(
        [FromBody] SimulateRoutingRequest request,
        CancellationToken ct)
    {
        var result = await routingService.SimulateRoutingAsync(request, ct);
        return Ok(result);
    }

    [HasPermission(AppModule.Settings, PermissionAction.View)]
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
