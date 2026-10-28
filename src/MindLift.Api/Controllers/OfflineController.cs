using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 6 / #43: Offline mode (PWA) (pwa-offline).
/// PWA webmanifest, offline bootstrap snapshot, delta sync, and mutation queue replay with conflict resolution.
/// </summary>
[ApiController]
[Route("api/v1/offline")]
[Authorize]
[RequiresFeature("pwa-offline")]
public sealed class OfflineController(IOfflineSyncService offlineService) : ControllerBase
{
    [HttpGet("manifest")]
    public async Task<ActionResult<PwaManifestDto>> GetManifest(CancellationToken ct)
    {
        var manifest = await offlineService.GetPwaManifestAsync(ct);
        return Ok(manifest);
    }

    [HttpGet("bootstrap")]
    public async Task<ActionResult<OfflineBootstrapDto>> GetBootstrap(CancellationToken ct)
    {
        var bootstrap = await offlineService.GetBootstrapAsync(ct);
        return Ok(bootstrap);
    }

    [HttpGet("delta")]
    public async Task<ActionResult<OfflineDeltaDto>> GetDelta([FromQuery] DateTime sinceUtc, CancellationToken ct)
    {
        var delta = await offlineService.GetDeltaAsync(sinceUtc, ct);
        return Ok(delta);
    }

    [HttpPost("sync")]
    public async Task<ActionResult<OfflineSyncBatchResultDto>> SyncBatch([FromBody] OfflineSyncBatchRequest request, CancellationToken ct)
    {
        var result = await offlineService.ProcessBatchSyncAsync(request, ct);
        return Ok(result);
    }
}
