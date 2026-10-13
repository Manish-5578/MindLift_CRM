using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Reporting;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/reports")]
public sealed class ReportsController(
    IReportService reportService,
    IReportDataSourceRegistry dataSourceRegistry,
    ISystemReportRegistry systemReportRegistry) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<ReportDefinitionDto>>> List(
        [FromQuery] Guid? folderId,
        [FromQuery] bool favoritesOnly = false,
        CancellationToken ct = default)
    {
        var list = await reportService.ListReportsAsync(folderId, favoritesOnly, ct);
        return Ok(list);
    }

    [HttpGet("sources")]
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    public ActionResult<IReadOnlyList<ReportDataSourceDto>> GetSources()
    {
        var sources = dataSourceRegistry.GetAll().Select(s => new ReportDataSourceDto
        {
            Key = s.Key,
            Label = s.Label,
            Module = s.Module.ToString(),
            DefaultDateField = s.DefaultDateField,
            Fields = s.Fields.Values.Select(f => new ReportFieldDto
            {
                Key = f.Key,
                Label = f.Label,
                Type = f.Type.ToString(),
                Groupable = f.Groupable,
                Aggregatable = f.Aggregatable
            }).ToList()
        }).ToList();

        return Ok(sources);
    }

    [HttpPost("run")]
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    public async Task<ActionResult<ReportResult>> RunAdHoc([FromBody] ReportQuery query, CancellationToken ct = default)
    {
        var result = await reportService.RunAdHocReportAsync(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    public async Task<ActionResult<ReportDefinitionDto>> Get(Guid id, CancellationToken ct = default)
    {
        var report = await reportService.GetReportAsync(id, ct);
        return Ok(report);
    }

    [HttpPost]
    [HasPermission(AppModule.Reports, PermissionAction.Create)]
    public async Task<ActionResult<ReportDefinitionDto>> Create([FromBody] SaveReportDefinitionRequest req, CancellationToken ct = default)
    {
        var report = await reportService.SaveReportAsync(null, req, ct);
        return CreatedAtAction(nameof(Get), new { id = report.Id }, report);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(AppModule.Reports, PermissionAction.Edit)]
    public async Task<ActionResult<ReportDefinitionDto>> Update(Guid id, [FromBody] SaveReportDefinitionRequest req, CancellationToken ct = default)
    {
        var report = await reportService.SaveReportAsync(id, req, ct);
        return Ok(report);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(AppModule.Reports, PermissionAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await reportService.DeleteReportAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/run")]
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    public async Task<ActionResult<ReportResult>> RunReport(Guid id, [FromBody] ReportRunRequest req, CancellationToken ct = default)
    {
        var result = await reportService.RunReportAsync(id, req, ct);
        return Ok(result);
    }

    [HttpGet("system/{key}")]
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    public ActionResult<ReportDefinitionDto> GetSystem(string key)
    {
        var sys = systemReportRegistry.Get(key);
        if (sys == null) return NotFound();

        return Ok(new ReportDefinitionDto
        {
            Id = Guid.Empty,
            Name = sys.Name,
            Description = sys.Description,
            Source = sys.Key,
            IsSystem = true,
            SystemKey = sys.Key,
            Visibility = ReportVisibility.Everyone,
            Query = new ReportQuery { Source = sys.Key, Visualization = new ReportVisualization { ChartType = sys.DefaultChartType } }
        });
    }

    [HttpPost("system/{key}/run")]
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    public async Task<ActionResult<ReportResult>> RunSystem(string key, [FromBody] ReportRunRequest req, CancellationToken ct = default)
    {
        var result = await reportService.RunSystemReportAsync(key, req, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/favorite")]
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    public async Task<ActionResult<bool>> ToggleFavorite(Guid id, CancellationToken ct = default)
    {
        var isFav = await reportService.ToggleFavoriteAsync(id, ct);
        return Ok(isFav);
    }

    [HttpGet("{id:guid}/export")]
    [HttpPost("{id:guid}/export")]
    [HasPermission(AppModule.Reports, PermissionAction.Export)]
    public async Task<IActionResult> Export(Guid id, [FromQuery] string format = "csv", [FromBody] ReportRunRequest? req = null, CancellationToken ct = default)
    {
        var r = await reportService.GetReportAsync(id, ct);
        var result = await reportService.RunReportAsync(id, req ?? new(), ct);
        var bytes = reportService.Export(result, format, r.Name);

        var (contentType, ext) = format.ToLowerInvariant() switch
        {
            "csv" => ("text/csv; charset=utf-8", "csv"),
            "pdf" => ("application/pdf", "pdf"),
            _ => ("application/vnd.ms-excel", "xml")
        };
        var fileName = $"{r.Name.Replace(" ", "_")}.{ext}";
        return File(bytes, contentType, fileName);
    }

    [HttpGet("system/{key}/export")]
    [HttpPost("system/{key}/export")]
    [HasPermission(AppModule.Reports, PermissionAction.Export)]
    public async Task<IActionResult> ExportSystem(string key, [FromQuery] string format = "csv", [FromBody] ReportRunRequest? req = null, CancellationToken ct = default)
    {
        var sys = systemReportRegistry.Get(key);
        if (sys == null) return NotFound();

        var result = await reportService.RunSystemReportAsync(key, req ?? new(), ct);
        var bytes = reportService.Export(result, format, sys.Name);

        var (contentType, ext) = format.ToLowerInvariant() switch
        {
            "csv" => ("text/csv; charset=utf-8", "csv"),
            "pdf" => ("application/pdf", "pdf"),
            _ => ("application/vnd.ms-excel", "xml")
        };
        var fileName = $"{sys.Name.Replace(" ", "_")}.{ext}";
        return File(bytes, contentType, fileName);
    }

    [HttpGet("{id:guid}/schedules")]
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<ReportScheduleDto>>> GetSchedules(Guid id, CancellationToken ct = default)
    {
        var list = await reportService.GetSchedulesAsync(id, ct);
        return Ok(list);
    }

    [HttpPost("{id:guid}/schedules")]
    [HasPermission(AppModule.Reports, PermissionAction.Edit)]
    public async Task<ActionResult<ReportScheduleDto>> CreateSchedule(Guid id, [FromBody] SaveReportScheduleRequest req, CancellationToken ct = default)
    {
        var sched = await reportService.SaveScheduleAsync(id, null, req, ct);
        return Ok(sched);
    }

    [HttpDelete("schedules/{id:guid}")]
    [HasPermission(AppModule.Reports, PermissionAction.Edit)]
    public async Task<IActionResult> DeleteSchedule(Guid id, CancellationToken ct = default)
    {
        await reportService.DeleteScheduleAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/v1/report-folders")]
public sealed class ReportFoldersController(IReportService reportService) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Reports, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<ReportFolderDto>>> List(CancellationToken ct = default)
    {
        var list = await reportService.ListFoldersAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    [HasPermission(AppModule.Reports, PermissionAction.Create)]
    public async Task<ActionResult<ReportFolderDto>> Create([FromBody] SaveReportFolderRequest req, CancellationToken ct = default)
    {
        var f = await reportService.SaveFolderAsync(null, req, ct);
        return Ok(f);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(AppModule.Reports, PermissionAction.Edit)]
    public async Task<ActionResult<ReportFolderDto>> Update(Guid id, [FromBody] SaveReportFolderRequest req, CancellationToken ct = default)
    {
        var f = await reportService.SaveFolderAsync(id, req, ct);
        return Ok(f);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(AppModule.Reports, PermissionAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await reportService.DeleteFolderAsync(id, ct);
        return NoContent();
    }
}
