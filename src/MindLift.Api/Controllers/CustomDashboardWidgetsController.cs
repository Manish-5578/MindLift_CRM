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
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomDashboardWidgetDto>>> List(CancellationToken ct)
    {
        var widgets = await widgetService.ListWidgetsAsync(ct);
        return Ok(widgets);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomDashboardWidgetDto>> Get(Guid id, CancellationToken ct)
    {
        var widget = await widgetService.GetWidgetAsync(id, ct);
        return Ok(widget);
    }

    [HttpPost]
    public async Task<ActionResult<CustomDashboardWidgetDto>> Create(
        [FromBody] SaveCustomDashboardWidgetRequest request,
        CancellationToken ct)
    {
        var widget = await widgetService.CreateWidgetAsync(request, ct);
        return Created($"/api/v1/custom-dashboard-widgets/{widget.Id}", widget);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomDashboardWidgetDto>> Update(
        Guid id,
        [FromBody] SaveCustomDashboardWidgetRequest request,
        CancellationToken ct)
    {
        var updated = await widgetService.UpdateWidgetAsync(id, request, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await widgetService.DeleteWidgetAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/data")]
    public async Task<ActionResult<CustomWidgetDataDto>> GetData(Guid id, CancellationToken ct)
    {
        var data = await widgetService.EvaluateDataAsync(id, ct);
        return Ok(data);
    }

    [HttpPost("preview")]
    public async Task<ActionResult<CustomWidgetDataDto>> Preview(
        [FromBody] SaveCustomDashboardWidgetRequest request,
        CancellationToken ct)
    {
        var data = await widgetService.PreviewDataAsync(request, ct);
        return Ok(data);
    }
}
