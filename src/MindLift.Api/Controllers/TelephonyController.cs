using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 5 / #32: Click-to-call & Telephony (click-to-call).
/// REST endpoints for telephony provider configuration, initiating outbound calls,
/// updating call status, logging dispositions, and call history tracking.
/// </summary>
[ApiController]
[Route("api/v1/telephony")]
public sealed class TelephonyController(
    ITelephonyService telephonyService,
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
    // Public Provider Status Callback Webhook
    // ==========================================

    [AllowAnonymous]
    [HttpPost("webhook/status")]
    public async Task<ActionResult<CallSessionDto>> StatusCallback(
        [FromBody] UpdateCallStatusRequest request,
        CancellationToken ct)
    {
        var updated = await telephonyService.UpdateCallStatusAsync(request, ct);
        return Ok(updated);
    }

    // ==========================================
    // Authenticated Endpoints (RequiresFeature "click-to-call")
    // ==========================================

    [Authorize]
    [RequiresFeature("click-to-call")]
    [HttpGet("settings")]
    public async Task<ActionResult<TelephonySettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await telephonyService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [Authorize]
    [RequiresFeature("click-to-call")]
    [HttpPut("settings")]
    public async Task<ActionResult<TelephonySettingsDto>> UpdateSettings(
        [FromBody] UpdateTelephonySettingsRequest request,
        CancellationToken ct)
    {
        var updated = await telephonyService.UpdateSettingsAsync(request, ct);
        return Ok(updated);
    }

    [Authorize]
    [RequiresFeature("click-to-call")]
    [HttpPost("calls")]
    public async Task<ActionResult<InitiateCallResult>> InitiateCall(
        [FromBody] InitiateCallRequest request,
        CancellationToken ct)
    {
        var result = await telephonyService.InitiateCallAsync(request, CurrentUserId(), ct);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [Authorize]
    [RequiresFeature("click-to-call")]
    [HttpGet("calls/{id:guid}")]
    public async Task<ActionResult<CallSessionDto>> GetCall(Guid id, CancellationToken ct)
    {
        var call = await telephonyService.GetCallAsync(id, ct);
        return Ok(call);
    }

    [Authorize]
    [RequiresFeature("click-to-call")]
    [HttpPost("calls/{id:guid}/end")]
    public async Task<ActionResult<CallSessionDto>> EndCall(
        Guid id,
        [FromQuery] int? durationSeconds = null,
        CancellationToken ct = default)
    {
        var call = await telephonyService.EndCallAsync(id, durationSeconds, ct);
        return Ok(call);
    }

    [Authorize]
    [RequiresFeature("click-to-call")]
    [HttpPost("calls/{id:guid}/disposition")]
    public async Task<ActionResult<CallSessionDto>> SetDisposition(
        Guid id,
        [FromBody] UpdateCallDispositionRequest request,
        CancellationToken ct)
    {
        var call = await telephonyService.SetDispositionAsync(id, request, ct);
        return Ok(call);
    }

    [Authorize]
    [RequiresFeature("click-to-call")]
    [HttpGet("calls")]
    public async Task<ActionResult<IReadOnlyList<CallSessionDto>>> GetCallHistory(
        [FromQuery] Guid? leadId = null,
        [FromQuery] Guid? contactId = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] Guid? dealId = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? status = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var query = new CallHistoryQuery(leadId, contactId, accountId, dealId, userId, status, limit);
        var calls = await telephonyService.GetCallHistoryAsync(query, ct);
        return Ok(calls);
    }
}
