using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 2 / #13: Scheduled Backups (scheduled-backups).
/// Endpoints for managing recurring backup schedules, triggering on-demand exports, and downloading archives.
/// </summary>
[ApiController]
[Route("api/v1/backups")]
[RequiresFeature("scheduled-backups")]
[Authorize]
public sealed class BackupsController(IBackupService backups) : ControllerBase
{
    [HttpGet("schedule")]
    public async Task<ActionResult<BackupScheduleConfigDto>> GetSchedule(CancellationToken cancellationToken)
        => Ok(await backups.GetScheduleConfigAsync(cancellationToken));

    [HttpPut("schedule")]
    public async Task<ActionResult<BackupScheduleConfigDto>> SaveSchedule(
        [FromBody] SaveBackupScheduleConfigRequest request,
        CancellationToken cancellationToken)
        => Ok(await backups.SaveScheduleConfigAsync(request, cancellationToken));

    [HttpGet("archives")]
    public async Task<ActionResult<IReadOnlyList<BackupArchiveDto>>> ListArchives(CancellationToken cancellationToken)
        => Ok(await backups.ListArchivesAsync(cancellationToken));

    [HttpGet("archives/{id:guid}")]
    public async Task<ActionResult<BackupArchiveDto>> GetArchive(Guid id, CancellationToken cancellationToken)
        => Ok(await backups.GetArchiveAsync(id, cancellationToken));

    [HttpPost("trigger")]
    public async Task<ActionResult<BackupArchiveDto>> Trigger(
        [FromBody] TriggerBackupRequest? request,
        CancellationToken cancellationToken)
    {
        var archive = await backups.TriggerBackupAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetArchive), new { id = archive.Id }, archive);
    }

    [HttpGet("archives/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var download = await backups.DownloadArchiveAsync(id, cancellationToken);
        return File(download.Data, download.ContentType, download.FileName);
    }

    [HttpDelete("archives/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await backups.DeleteArchiveAsync(id, cancellationToken);
        return NoContent();
    }
}
