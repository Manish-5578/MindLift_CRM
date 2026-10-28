using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 5 / #31: BCC Dropbox (bcc-dropbox).
/// REST endpoints for managing reps' dedicated BCC dropbox addresses, tenant routing settings,
/// and accepting public webhook/inbound email deliveries.
/// </summary>
[ApiController]
[Route("api/v1/bcc-dropbox")]
public sealed class BccDropboxController(
    IBccDropboxService dropboxService,
    ITenantContext tenantContext) : ControllerBase
{
    private Guid CurrentUserId()
    {
        if (tenantContext.UserId is { } id && id != Guid.Empty)
            return id;

        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(sub, out var parsed))
            return parsed;

        throw new UnauthorizedException("not_signed_in", "User is not signed in.");
    }

    // ==========================================
    // Public / Inbound Webhook Ingestion Endpoint
    // ==========================================

    [AllowAnonymous]
    [HttpPost("inbound")]
    public async Task<ActionResult<InboundBccEmailResult>> ProcessInboundEmail(
        [FromBody] InboundBccEmailRequest request,
        CancellationToken ct)
    {
        var result = await dropboxService.ProcessInboundEmailAsync(request, ct);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    // ==========================================
    // Authenticated Management Endpoints
    // ==========================================

    [Authorize]
    [RequiresFeature("bcc-dropbox")]
    [HttpGet("my-address")]
    public async Task<ActionResult<BccDropboxAddressDto>> GetMyAddress(CancellationToken ct)
    {
        var address = await dropboxService.GetOrCreateUserDropboxAddressAsync(CurrentUserId(), ct);
        return Ok(address);
    }

    [Authorize]
    [RequiresFeature("bcc-dropbox")]
    [HttpPost("regenerate-address")]
    public async Task<ActionResult<BccDropboxAddressDto>> RegenerateAddress(CancellationToken ct)
    {
        var address = await dropboxService.RegenerateUserDropboxAddressAsync(CurrentUserId(), ct);
        return Ok(address);
    }

    [Authorize]
    [RequiresFeature("bcc-dropbox")]
    [HttpGet("settings")]
    public async Task<ActionResult<BccDropboxSettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await dropboxService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [Authorize]
    [RequiresFeature("bcc-dropbox")]
    [HttpPut("settings")]
    public async Task<ActionResult<BccDropboxSettingsDto>> UpdateSettings(
        [FromBody] UpdateBccDropboxSettingsRequest request,
        CancellationToken ct)
    {
        var updated = await dropboxService.UpdateSettingsAsync(request, ct);
        return Ok(updated);
    }

    [Authorize]
    [RequiresFeature("bcc-dropbox")]
    [HttpGet("logs")]
    public async Task<ActionResult<IReadOnlyList<BccDropboxLogDto>>> ListLogs(
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var logs = await dropboxService.ListLogsAsync(limit, ct);
        return Ok(logs);
    }
}
