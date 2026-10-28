using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Activities;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 1 / #7: 1-Click Quick-Log Dock (quick-log-dock).
/// Provides rapid logging of calls, notes, tasks, and meetings directly from record views and floating docks.
/// </summary>
[ApiController]
[Route("api/v1/quick-log")]
[RequiresFeature("quick-log-dock")]
public sealed class QuickLogController(QuickLogService quickLog) : ControllerBase
{
    [HasPermission(AppModule.Activities, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<QuickLogResult>> Log([FromBody] QuickLogRequest request, CancellationToken cancellationToken)
        => Ok(await quickLog.LogAsync(request, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.Create)]
    [HttpPost("call")]
    public async Task<ActionResult<QuickLogResult>> LogCall([FromBody] QuickLogCallRequest request, CancellationToken cancellationToken)
        => Ok(await quickLog.LogCallAsync(request, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.Create)]
    [HttpPost("note")]
    public async Task<ActionResult<QuickLogResult>> LogNote([FromBody] QuickLogNoteRequest request, CancellationToken cancellationToken)
        => Ok(await quickLog.LogNoteAsync(request, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.Create)]
    [HttpPost("task")]
    public async Task<ActionResult<QuickLogResult>> LogTask([FromBody] QuickLogTaskRequest request, CancellationToken cancellationToken)
        => Ok(await quickLog.LogTaskAsync(request, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.Create)]
    [HttpPost("meeting")]
    public async Task<ActionResult<QuickLogResult>> LogMeeting([FromBody] QuickLogMeetingRequest request, CancellationToken cancellationToken)
        => Ok(await quickLog.LogMeetingAsync(request, cancellationToken));
}
