using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
    [HasPermission(AppModule.Dashboard, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> GetDashboard(
        [FromQuery] DashboardView? view,
        [FromQuery] string? period,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? teamId,
        [FromQuery] Guid? userId,
        [FromQuery] Guid? pipelineId,
        [FromQuery] bool refresh = false,
        CancellationToken cancellationToken = default
    ) => Ok(await dashboard.GetDashboardAsync(view, period, from, to, teamId, userId, pipelineId, refresh, cancellationToken));

    [HasPermission(AppModule.Dashboard, PermissionAction.View)]
    [HttpGet("widgets/{key}")]
    public async Task<ActionResult<DashboardWidgetDto>> GetWidget(
        string key,
        [FromQuery] DashboardView? view,
        [FromQuery] string? period,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? teamId,
        [FromQuery] Guid? userId,
        [FromQuery] Guid? pipelineId,
        [FromQuery] bool refresh = false,
        CancellationToken cancellationToken = default
    ) => Ok(await dashboard.GetWidgetAsync(key, view, period, from, to, teamId, userId, pipelineId, refresh, cancellationToken));

    [HasPermission(AppModule.Dashboard, PermissionAction.View)]
    [HttpGet("layout")]
    public async Task<ActionResult<DashboardLayoutDto>> GetLayout(
        [FromQuery] DashboardView view = DashboardView.Staff,
        CancellationToken cancellationToken = default
    ) => Ok(await dashboard.GetLayoutAsync(view, cancellationToken));

    [HasPermission(AppModule.Dashboard, PermissionAction.Edit)]
    [HttpPut("layout")]
    public async Task<ActionResult<DashboardLayoutDto>> SaveLayout(
        [FromBody] SaveDashboardLayoutRequest request,
        CancellationToken cancellationToken = default
    ) => Ok(await dashboard.SaveLayoutAsync(request, cancellationToken));

    [HasPermission(AppModule.Dashboard, PermissionAction.Edit)]
    [HttpDelete("layout")]
    public async Task<ActionResult<DashboardLayoutDto>> ResetLayout(
        [FromQuery] DashboardView view = DashboardView.Staff,
        CancellationToken cancellationToken = default
    ) => Ok(await dashboard.ResetLayoutAsync(view, cancellationToken));
}

[ApiController]
[Route("api/v1/sales-targets")]
public sealed class SalesTargetsController(ISalesTargetService targets) : ControllerBase
{
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SalesTargetDto>>> List(CancellationToken cancellationToken = default)
        => Ok(await targets.ListAsync(cancellationToken));

    [HasPermission(AppModule.Reports, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SalesTargetDto>> Get(Guid id, CancellationToken cancellationToken = default)
        => Ok(await targets.GetByIdAsync(id, cancellationToken));

    [HasPermission(AppModule.Reports, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<SalesTargetDto>> Create([FromBody] SaveSalesTargetRequest request, CancellationToken cancellationToken = default)
        => Ok(await targets.SaveAsync(null, request, cancellationToken));

    [HasPermission(AppModule.Reports, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SalesTargetDto>> Update(Guid id, [FromBody] SaveSalesTargetRequest request, CancellationToken cancellationToken = default)
        => Ok(await targets.SaveAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Reports, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await targets.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
