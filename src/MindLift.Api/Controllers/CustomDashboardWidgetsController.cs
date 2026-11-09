using MindLift.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 6 / #40: Custom dashboard widgets (dashboard-widgets).
/// Management and dynamic query evaluation for custom user-configured dashboard cards.
/// </summary>
[ApiController]
[Route("api/v1/custom-dashboard-widgets")]
[Authorize]
[RequiresFeature("dashboard-widgets")]
public sealed class CustomDashboardWidgetsController(ICustomDashboardWidgetService widgetService) : ControllerBase
{
    [HasPermission(AppModule.Dashboard, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomDashboardWidgetDto>>> List(CancellationToken ct)
    {
        var widgets = await widgetService.ListWidgetsAsync(ct);
        return Ok(widgets);
    }

    [HasPermission(AppModule.Dashboard, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomDashboardWidgetDto>> Get(Guid id, CancellationToken ct)
    {
        var widget = await widgetService.GetWidgetAsync(id, ct);
        return Ok(widget);
    }

    [HasPermission(AppModule.Dashboard, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<CustomDashboardWidgetDto>> Create(
        [FromBody] SaveCustomDashboardWidgetRequest request,
        CancellationToken ct)
    {
        var widget = await widgetService.CreateWidgetAsync(request, ct);
        return Created($"/api/v1/custom-dashboard-widgets/{widget.Id}", widget);
    }

    [HasPermission(AppModule.Dashboard, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomDashboardWidgetDto>> Update(
        Guid id,
        [FromBody] SaveCustomDashboardWidgetRequest request,
        CancellationToken ct)
    {
        var updated = await widgetService.UpdateWidgetAsync(id, request, ct);
        return Ok(updated);
    }

    [HasPermission(AppModule.Dashboard, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await widgetService.DeleteWidgetAsync(id, ct);
        return NoContent();
    }

    [HasPermission(AppModule.Dashboard, PermissionAction.View)]
    [HttpGet("{id:guid}/data")]
    public async Task<ActionResult<CustomWidgetDataDto>> GetData(Guid id, CancellationToken ct)
    {
        var data = await widgetService.EvaluateDataAsync(id, ct);
        return Ok(data);
    }

    [HasPermission(AppModule.Dashboard, PermissionAction.View)]
    [HttpPost("preview")]
    public async Task<ActionResult<CustomWidgetDataDto>> Preview(
        [FromBody] SaveCustomDashboardWidgetRequest request,
        CancellationToken ct)
    {
        var data = await widgetService.PreviewDataAsync(request, ct);
        return Ok(data);
    }
}
