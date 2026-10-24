using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.DataPortability;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>A company's own copy of all its data. Only people who manage the company's settings can ask for it.</summary>
[ApiController]
[Route("api/v1/settings/data")]
[Authorize]
public sealed class DataController(CompanyDataService data, ITenantContext tenant) : ControllerBase
{
    private Guid TenantId => tenant.TenantId ?? throw new UnauthorizedAccessException();

    [HttpGet("exports")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<IReadOnlyList<DataExportDto>>> Exports(CancellationToken cancellationToken) => Ok(await data.ExportsAsync(TenantId, cancellationToken));

    [HttpPost("exports")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<DataExportDto>> Export(CancellationToken cancellationToken) => Ok(await data.RequestExportAsync(TenantId, cancellationToken));

    /// <summary>A ten-minute link that downloads the export straight to the browser.</summary>
    [HttpPost("exports/{id:guid}/link")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<DownloadLinkDto>> Link(Guid id, CancellationToken cancellationToken) => Ok(new DownloadLinkDto(await data.DownloadLinkAsync(TenantId, id, cancellationToken)));

    [HttpGet("exports/{id:guid}/download")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var (stream, name) = await data.OpenExportAsync(TenantId, id, cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, "application/zip", name);
    }
}

/// <summary>Where a signed download link leads. The link itself is the permission; it names one export and expires after ten minutes.</summary>
[ApiController]
[AllowAnonymous]
public sealed class DownloadsController(CompanyDataService data) : ControllerBase
{
    [HttpGet("/downloads/export")]
    public async Task<IActionResult> Export([FromQuery] string? t, CancellationToken cancellationToken)
    {
        var file = string.IsNullOrEmpty(t) ? null : await data.OpenByLinkAsync(t, cancellationToken);
        if (file is null)
        {
            return NotFound("This download link is not valid or has expired. Prepare a new link.");
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "no-store";
        return File(file.Value.Stream, "application/zip", file.Value.FileName);
    }
}
