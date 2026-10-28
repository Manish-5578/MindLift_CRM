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

    [HasPermission(AppModule.Users, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<UserDetail>> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
        => Ok(await users.CreateAsync(request, cancellationToken));

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

/// <summary>
/// The platform console (plan 6.10): every company, what each uses, which modules each has, plans and the history of changes.
/// Requires a platform-admin token. A platform token does not grant a company's own data.
/// </summary>
[ApiController]
[Route("api/v1/platform")]
[Authorize(Policy = "PlatformAdmin")]
public sealed class PlatformController(PlatformService platform, MindLift.Infrastructure.Platform.PlatformConsole console, MindLift.Infrastructure.Billing.BillingService billing, MindLift.Infrastructure.DataPortability.CompanyDataService data) : ControllerBase
{
    /// <summary>Opens a read-only support view of the company, as its owner sees it. A reason is required and kept.</summary>
    [HttpPost("companies/{id:guid}/support-sessions")]
    public async Task<ActionResult<SupportSessionStartedDto>> StartSupport(Guid id, [FromBody] StartSupportSessionRequest request, [FromServices] MindLift.Infrastructure.Platform.SupportAccessService support, CancellationToken cancellationToken)
        => Ok(await support.StartAsync(id, request, cancellationToken));

    [HttpGet("companies/{id:guid}/support-sessions")]
    public async Task<ActionResult<IReadOnlyList<SupportSessionDto>>> SupportSessions(Guid id, [FromServices] MindLift.Infrastructure.Platform.SupportAccessService support, CancellationToken cancellationToken)
        => Ok(await support.SessionsAsync(id, cancellationToken));

    [HttpPost("support-sessions/{sessionId:guid}/end")]
    public async Task<IActionResult> EndSupport(Guid sessionId, [FromServices] MindLift.Infrastructure.Platform.SupportAccessService support, CancellationToken cancellationToken)
    {
        await support.EndAsync(sessionId, cancellationToken);
        return NoContent();
    }

    /// <summary>All of one company's data as a download, for a backup or when it leaves.</summary>
    [HttpPost("companies/{id:guid}/exports")]
    public async Task<ActionResult<DataExportDto>> ExportCompany(Guid id, CancellationToken cancellationToken) => Ok(await data.RequestExportAsync(id, cancellationToken));

    [HttpGet("companies/{id:guid}/exports")]
    public async Task<ActionResult<IReadOnlyList<DataExportDto>>> CompanyExports(Guid id, CancellationToken cancellationToken) => Ok(await data.ExportsAsync(id, cancellationToken));

    [HttpGet("companies/{id:guid}/exports/{exportId:guid}/download")]
    public async Task<IActionResult> DownloadCompanyExport(Guid id, Guid exportId, CancellationToken cancellationToken)
    {
        var (stream, name) = await data.OpenExportAsync(id, exportId, cancellationToken);
        return File(stream, "application/zip", name);
    }

    [HttpPost("companies/{id:guid}/exports/{exportId:guid}/link")]
    public async Task<ActionResult<DownloadLinkDto>> CompanyExportLink(Guid id, Guid exportId, CancellationToken cancellationToken) => Ok(new DownloadLinkDto(await data.DownloadLinkAsync(id, exportId, cancellationToken)));

    /// <summary>Removes a company and all its data for good. The body must name the company's address (slug).</summary>
    [HttpDelete("companies/{id:guid}")]
    public async Task<IActionResult> PurgeCompany(Guid id, [FromBody] PurgeCompanyRequest request, CancellationToken cancellationToken)
    {
        var actor = User.FindFirst("name")?.Value is { Length: > 0 } name ? name : "Platform";
        await data.RequestPurgeAsync(id, request.Confirm, actor, cancellationToken);
        return Accepted();
    }

    /// <summary>A company's subscription and invoices.</summary>
    [HttpGet("companies/{id:guid}/billing")]
    public async Task<ActionResult<PlatformBillingDto>> Billing(Guid id, CancellationToken cancellationToken)
        => Ok(await billing.PlatformViewAsync(id, cancellationToken));

    /// <summary>Records money received from a company (bank transfer, cash, cheque).</summary>
    [HttpPost("companies/{id:guid}/billing/payments")]
    public async Task<ActionResult<PlatformBillingDto>> RecordPayment(Guid id, [FromBody] RecordBillingPaymentRequest request, CancellationToken cancellationToken)
        => Ok(await billing.RecordPaymentAsync(id, request, cancellationToken));

    [HttpPost("companies/{id:guid}/billing/invoices/{invoiceId:guid}/void")]
    public async Task<ActionResult<PlatformBillingDto>> VoidInvoice(Guid id, Guid invoiceId, CancellationToken cancellationToken)
        => Ok(await billing.VoidInvoiceAsync(id, invoiceId, cancellationToken));

    /// <summary>The numbers for the whole platform: companies by status and plan, signups, usage, what needs attention.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<PlatformOverviewDto>> Overview(CancellationToken cancellationToken)
        => Ok(await console.OverviewAsync(cancellationToken));

    [HttpGet("companies")]
    public async Task<ActionResult<PlatformCompanyListDto>> Companies(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? plan, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default, [FromQuery] string? activity = null)
        => Ok(await console.ListCompaniesAsync(search, status, plan, sort, page, pageSize, cancellationToken, activity));

    /// <summary>Onboards a new company with its owner, plan and modules.</summary>
    [HttpPost("companies")]
    public async Task<ActionResult<PlatformCompanyDetailDto>> CreateCompany([FromBody] CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        var created = await console.CreateCompanyAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Company), new { id = created.Company.Id }, created);
    }

    [HttpGet("companies/{id:guid}")]
    public async Task<ActionResult<PlatformCompanyDetailDto>> Company(Guid id, CancellationToken cancellationToken)
        => Ok(await console.GetCompanyAsync(id, cancellationToken));

    /// <summary>Sets which modules are switched off for a company. Everything not listed stays available.</summary>
    [HttpPut("companies/{id:guid}/modules")]
    public async Task<ActionResult<PlatformCompanyDetailDto>> SetModules(Guid id, [FromBody] SetCompanyModulesRequest request, CancellationToken cancellationToken)
        => Ok(await console.SetModulesAsync(id, request, cancellationToken));

    [HttpPut("companies/{id:guid}/limits")]
    public async Task<ActionResult<PlatformCompanyDetailDto>> SetLimits(Guid id, [FromBody] SetCompanyLimitsRequest request, CancellationToken cancellationToken)
        => Ok(await console.SetLimitsAsync(id, request, cancellationToken));

    [HttpPatch("companies/{id:guid}/status")]
    public async Task<IActionResult> CompanyStatus(Guid id, [FromBody] ChangeTenantStatusRequest request, CancellationToken cancellationToken)
    {
        await console.ChangeStatusAsync(id, request.Status, cancellationToken);
        return NoContent();
    }

    [HttpPatch("companies/{id:guid}/plan")]
    public async Task<IActionResult> CompanyPlan(Guid id, [FromBody] ChangeTenantPlanRequest request, CancellationToken cancellationToken)
    {
        await console.ChangePlanAsync(id, request.PlanKey, cancellationToken);
        return NoContent();
    }

    /// <summary>Every module with how many companies have it.</summary>
    [HttpGet("modules")]
    public async Task<ActionResult<IReadOnlyList<PlatformModuleDto>>> Modules(CancellationToken cancellationToken)
        => Ok(await console.ModulesAsync(cancellationToken));

    /// <summary>The ready-made choices of modules.</summary>
    [HttpGet("module-presets")]
    public ActionResult<IReadOnlyList<ModulePresetDto>> ModulePresets()
        => Ok(ModuleCatalog.Presets.Select(p => new ModulePresetDto(p.Key, p.Name, p.Description, p.Modules.Select(m => m.ToString()).ToList())).ToList());

    [HttpGet("plans")]
    public async Task<ActionResult<IReadOnlyList<PlatformPlanDto>>> Plans(CancellationToken cancellationToken)
        => Ok(await console.PlansAsync(cancellationToken));

    [HttpPost("plans")]
    public async Task<ActionResult<PlatformPlanDto>> CreatePlan([FromBody] SavePlanRequest request, CancellationToken cancellationToken)
        => Ok(await console.CreatePlanAsync(request, cancellationToken));

    [HttpDelete("plans/{id:guid}")]
    public async Task<IActionResult> DeletePlan(Guid id, CancellationToken cancellationToken)
    {
        await console.DeletePlanAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("plans/{id:guid}")]
    public async Task<ActionResult<PlatformPlanDto>> SavePlan(Guid id, [FromBody] SavePlanRequest request, CancellationToken cancellationToken)
        => Ok(await console.SavePlanAsync(id, request, cancellationToken));

    /// <summary>The history of what the platform owner changed.</summary>
    [HttpGet("activity")]
    public async Task<ActionResult<PagedResult<PlatformAuditDto>>> Activity(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, [FromQuery] Guid? company = null, [FromQuery] string? kind = null,
        [FromQuery] string? search = null, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken cancellationToken = default)
        => Ok(await console.ActivityAsync(page, pageSize, cancellationToken, company, kind, search, from, to));

    /// <summary>The audit trail as a CSV file.</summary>
    [HttpGet("activity/export")]
    public async Task<IActionResult> ExportActivity(
        [FromQuery] Guid? company = null, [FromQuery] string? kind = null, [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken cancellationToken = default)
        => File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(await console.ExportActivityAsync(company, kind, search, from, to, cancellationToken))).ToArray(), "text/csv", "platform-audit-trail.csv");

    /// <summary>New companies and companies that need a look.</summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<IReadOnlyList<PlatformNotificationDto>>> Notifications(CancellationToken cancellationToken)
        => Ok(await console.NotificationsAsync(cancellationToken));

    // The first version of the platform API, kept for existing callers.
    [HttpGet("tenants")]
    public async Task<ActionResult<PagedResult<PlatformTenantDto>>> ListTenants([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
        => Ok(await platform.ListTenantsAsync(search, page, pageSize, cancellationToken));

    [HttpPatch("tenants/{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeTenantStatusRequest request, CancellationToken cancellationToken)
    {
        await console.ChangeStatusAsync(id, request.Status, cancellationToken);
        return NoContent();
    }

    [HttpPatch("tenants/{id:guid}/plan")]
    public async Task<IActionResult> ChangePlan(Guid id, [FromBody] ChangeTenantPlanRequest request, CancellationToken cancellationToken)
    {
        await console.ChangePlanAsync(id, request.PlanKey, cancellationToken);
        return NoContent();
    }
}
