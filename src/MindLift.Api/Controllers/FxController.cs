using MindLift.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 3 / #16: Live Exchange Rate Sync (fx-sync).
/// Endpoints for foreign exchange rate synchronization, conversion, and audit status.
/// </summary>
[ApiController]
[Route("api/v1/fx")]
[RequiresFeature("fx-sync")]
[Authorize]
public sealed class FxController(IFxSyncService fx) : ControllerBase
{
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("sync")]
    public async Task<ActionResult<FxSyncResultDto>> Sync(
        [FromBody] FxSyncRequest? request,
        CancellationToken cancellationToken)
        => Ok(await fx.SyncRatesAsync(request, cancellationToken));

    [SignedInUser]
    [HttpGet("status")]
    public async Task<ActionResult<FxSyncStatusDto>> GetStatus(
        CancellationToken cancellationToken)
        => Ok(await fx.GetSyncStatusAsync(cancellationToken));

    [SignedInUser]
    [HttpGet("rates")]
    public async Task<ActionResult<IReadOnlyList<FxRateDto>>> GetRates(
        CancellationToken cancellationToken)
        => Ok(await fx.GetRatesAsync(cancellationToken));

    [SignedInUser]
    [HttpPost("convert")]
    public async Task<ActionResult<FxConvertResultDto>> Convert(
        [FromBody] FxConvertRequest request,
        CancellationToken cancellationToken)
        => Ok(await fx.ConvertCurrencyAsync(request, cancellationToken));
}
