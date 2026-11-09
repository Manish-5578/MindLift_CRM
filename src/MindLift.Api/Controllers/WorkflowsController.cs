using MindLift.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 2 / #12: Visual Workflow Builder (workflow-builder).
/// Endpoints for authoring visual DAG workflows, traversing branching logic, and running simulations.
/// </summary>
[ApiController]
[Route("api/v1/workflows")]
[RequiresFeature("workflow-builder")]
[Authorize]
public sealed class WorkflowsController(IVisualWorkflowService workflows) : ControllerBase
{
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VisualWorkflowDto>>> List(
        [FromQuery] string? entityType,
        CancellationToken cancellationToken)
        => Ok(await workflows.ListAsync(entityType, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VisualWorkflowDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await workflows.GetByIdAsync(id, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<VisualWorkflowDto>> Create(
        [FromBody] SaveVisualWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var created = await workflows.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VisualWorkflowDto>> Update(
        Guid id,
        [FromBody] SaveVisualWorkflowRequest request,
        CancellationToken cancellationToken)
        => Ok(await workflows.UpdateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await workflows.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpPost("{id:guid}/simulate")]
    public async Task<ActionResult<WorkflowSimulationResult>> Simulate(
        Guid id,
        [FromBody] SimulateWorkflowRequest request,
        CancellationToken cancellationToken)
        => Ok(await workflows.SimulateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("{id:guid}/executions")]
    public async Task<ActionResult<IReadOnlyList<VisualWorkflowExecutionDto>>> ListExecutions(
        Guid id,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
        => Ok(await workflows.ListExecutionsAsync(id, limit, cancellationToken));
}
