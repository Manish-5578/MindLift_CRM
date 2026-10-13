using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Administration;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(UserAdministrationService users) : ControllerBase
{
    [HasPermission(AppModule.Users, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserListItem>>> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
        => Ok(await users.ListAsync(search, page, pageSize, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDetail>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await users.GetAsync(id, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDetail>> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
        => Ok(await users.UpdateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Edit)]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, [FromBody] DeactivateUserRequest? request, CancellationToken cancellationToken)
    {
        await users.DeactivateAsync(id, request?.ReassignToUserId, cancellationToken);
        return NoContent();
    }

    [HasPermission(AppModule.Users, PermissionAction.Edit)]
    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        await users.ReactivateAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/invitations")]
public sealed class InvitationsController(InvitationService invitations) : ControllerBase
{
    [HasPermission(AppModule.Users, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvitationDto>>> List(CancellationToken cancellationToken)
        => Ok(await invitations.ListAsync(cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<InvitationDto>> Create([FromBody] CreateInvitationRequest request, CancellationToken cancellationToken)
        => Ok(await invitations.CreateAsync(request, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Edit)]
    [HttpPost("{id:guid}/resend")]
    public async Task<ActionResult<InvitationDto>> Resend(Guid id, CancellationToken cancellationToken)
        => Ok(await invitations.ResendAsync(id, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        await invitations.RevokeAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/roles")]
public sealed class RolesController(RoleAdministrationService roles) : ControllerBase
{
    [HasPermission(AppModule.Users, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken cancellationToken)
        => Ok(await roles.ListAsync(cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create([FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
        => Ok(await roles.CreateAsync(request, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RoleDto>> Update(Guid id, [FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
        => Ok(await roles.UpdateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await roles.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HasPermission(AppModule.Users, PermissionAction.View)]
    [HttpGet("{id:guid}/permissions")]
    public async Task<ActionResult<IReadOnlyList<RolePermissionDto>>> GetPermissions(Guid id, CancellationToken cancellationToken)
        => Ok(await roles.GetPermissionsAsync(id, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Edit)]
    [HttpPut("{id:guid}/permissions")]
    public async Task<ActionResult<IReadOnlyList<RolePermissionDto>>> SavePermissions(Guid id, [FromBody] SaveRolePermissionsRequest request, CancellationToken cancellationToken)
        => Ok(await roles.SavePermissionsAsync(id, request.Permissions, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Create)]
    [HttpPost("{id:guid}/clone")]
    public async Task<ActionResult<RoleDto>> Clone(Guid id, [FromBody] SaveRoleRequest request, CancellationToken cancellationToken)
        => Ok(await roles.CloneAsync(id, request.Name, cancellationToken));
}

[ApiController]
[Route("api/v1/teams")]
public sealed class TeamsController(TeamBranchService teams) : ControllerBase
{
    [HasPermission(AppModule.Users, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TeamDto>>> List(CancellationToken cancellationToken)
        => Ok(await teams.ListTeamsAsync(cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<TeamDto>> Create([FromBody] SaveTeamRequest request, CancellationToken cancellationToken)
        => Ok(await teams.CreateTeamAsync(request, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TeamDto>> Update(Guid id, [FromBody] SaveTeamRequest request, CancellationToken cancellationToken)
        => Ok(await teams.UpdateTeamAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await teams.DeleteTeamAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/branches")]
public sealed class BranchesController(TeamBranchService teams) : ControllerBase
{
    [HasPermission(AppModule.Users, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BranchDto>>> List(CancellationToken cancellationToken)
        => Ok(await teams.ListBranchesAsync(cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<BranchDto>> Create([FromBody] SaveBranchRequest request, CancellationToken cancellationToken)
        => Ok(await teams.CreateBranchAsync(request, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BranchDto>> Update(Guid id, [FromBody] SaveBranchRequest request, CancellationToken cancellationToken)
        => Ok(await teams.UpdateBranchAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await teams.DeleteBranchAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController(AuditLogQuery audit) : ControllerBase
{
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List([FromQuery] string? entityType, [FromQuery] Guid? entityId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
        => Ok(await audit.ListAsync(entityType, entityId, page, pageSize, cancellationToken));
}

/// <summary>Cross-tenant platform administration (plan 6.10). Requires a platform-admin token.</summary>
[ApiController]
[Route("api/v1/platform")]
[Authorize(Policy = "PlatformAdmin")]
public sealed class PlatformController(PlatformService platform) : ControllerBase
{
    [HttpGet("tenants")]
    public async Task<ActionResult<PagedResult<PlatformTenantDto>>> ListTenants([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
        => Ok(await platform.ListTenantsAsync(search, page, pageSize, cancellationToken));

    [HttpPatch("tenants/{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeTenantStatusRequest request, CancellationToken cancellationToken)
    {
        await platform.ChangeStatusAsync(id, request.Status, cancellationToken);
        return NoContent();
    }

    [HttpPatch("tenants/{id:guid}/plan")]
    public async Task<IActionResult> ChangePlan(Guid id, [FromBody] ChangeTenantPlanRequest request, CancellationToken cancellationToken)
    {
        await platform.ChangePlanAsync(id, request.PlanKey, cancellationToken);
        return NoContent();
    }
}
