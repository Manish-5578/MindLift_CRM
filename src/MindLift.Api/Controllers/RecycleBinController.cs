using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 2 / #8: Recycle Bin (recycle-bin).
/// Provides endpoints to view, restore, and permanently purge soft-deleted records within a 60-day window.
/// </summary>
[ApiController]
[Route("api/v1/recycle-bin")]
[RequiresFeature("recycle-bin")]
[Authorize]
public sealed class RecycleBinController(IRecycleBinService recycleBin) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RecycleBinPagedResult>> List([FromQuery] RecycleBinQuery query, CancellationToken cancellationToken)
        => Ok(await recycleBin.ListAsync(query, cancellationToken));

    [HttpPost("restore")]
    public async Task<ActionResult<RecycleBinOperationResult>> Restore([FromBody] RestoreRecordsRequest request, CancellationToken cancellationToken)
        => Ok(await recycleBin.RestoreAsync(request, cancellationToken));

    [HttpPost("purge")]
    public async Task<ActionResult<RecycleBinOperationResult>> Purge([FromBody] PurgeRecordsRequest request, CancellationToken cancellationToken)
        => Ok(await recycleBin.PurgeAsync(request, cancellationToken));
}
