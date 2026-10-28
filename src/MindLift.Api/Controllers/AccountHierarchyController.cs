using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 6 / #37: Account hierarchy tree (account-hierarchy).
/// Multi-level tree navigation, circular dependency validation, and subtree rollup metrics.
/// </summary>
[ApiController]
[Route("api/v1/accounts/{id:guid}/hierarchy")]
[Authorize]
[RequiresFeature("account-hierarchy")]
public sealed class AccountHierarchyController(IAccountHierarchyService hierarchyService) : ControllerBase
{
    [HasPermission(AppModule.Accounts, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<AccountHierarchyTreeDto>> GetHierarchy(
        Guid id,
        CancellationToken ct)
    {
        var tree = await hierarchyService.GetHierarchyAsync(id, ct);
        return Ok(tree);
    }

    [HasPermission(AppModule.Accounts, PermissionAction.View)]
    [HttpGet("rollups")]
    public async Task<ActionResult<AccountHierarchyMetricsDto>> GetRollups(
        Guid id,
        CancellationToken ct)
    {
        var rollups = await hierarchyService.GetRollupsAsync(id, ct);
        return Ok(rollups);
    }

    [HasPermission(AppModule.Accounts, PermissionAction.Edit)]
    [HttpPut("parent")]
    public async Task<IActionResult> SetParent(
        Guid id,
        [FromBody] SetAccountParentRequest request,
        CancellationToken ct)
    {
        await hierarchyService.SetParentAsync(id, request.ParentAccountId, ct);
        return NoContent();
    }
}
