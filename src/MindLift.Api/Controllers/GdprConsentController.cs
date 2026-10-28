using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 4 / #27: GDPR and unsubscribe center (unsubscribe-center).
/// REST endpoints for public preference center, RFC 8058 one-click unsubscribe,
/// GDPR Article 7 consent auditing, Article 15/20 data export, and Article 17 erasure.
/// </summary>
[ApiController]
[Route("api/v1/consent")]
public sealed class GdprConsentController(IGdprConsentService consentService) : ControllerBase
{
    // ================= Public Preference Center (Token Authenticated) =================

    [AllowAnonymous]
    [HttpGet("preferences")]
    public async Task<ActionResult<PreferenceCenterDetailsDto>> GetPreferences(
        [FromQuery] string token,
        CancellationToken ct)
    {
        var details = await consentService.GetPreferenceCenterAsync(token, ct);
        if (details == null) return NotFound(new { error = "Invalid or expired preference center link." });
        return Ok(details);
    }

    [AllowAnonymous]
    [HttpPost("preferences")]
    public async Task<IActionResult> UpdatePreferences(
        [FromQuery] string token,
        [FromBody] UpdatePreferencesRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = HttpContext.Request.Headers.UserAgent.ToString();

        var success = await consentService.UpdatePreferencesAsync(token, request, ip, ua, ct);
        if (!success) return NotFound(new { error = "Invalid or expired preference center link." });
        return Ok(new { success = true, message = "Preferences updated successfully." });
    }

    [AllowAnonymous]
    [HttpPost("one-click-unsubscribe")]
    public async Task<IActionResult> OneClickUnsubscribe(
        [FromQuery] string token,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = HttpContext.Request.Headers.UserAgent.ToString();

        var success = await consentService.ProcessOneClickUnsubscribeAsync(token, ip, ua, ct);
        if (!success) return NotFound(new { error = "Invalid or expired unsubscribe link." });
        return Ok(new { success = true, message = "You have been unsubscribed." });
    }

    // ================= Authenticated Consent & GDPR Tools =================

    [Authorize]
    [RequiresFeature("unsubscribe-center")]
    [HttpPost("tokens/generate")]
    public async Task<ActionResult<PreferenceTokenDto>> GenerateToken(
        [FromBody] GeneratePreferenceTokenRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required." });

        var token = await consentService.GeneratePreferenceTokenAsync(request, ct);
        return Ok(token);
    }

    [Authorize]
    [RequiresFeature("unsubscribe-center")]
    [HttpGet("check")]
    public async Task<ActionResult<ConsentCheckResult>> CheckConsent(
        [FromQuery] string email,
        [FromQuery] CommunicationChannel channel = CommunicationChannel.Email,
        [FromQuery] string topic = "Marketing",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Email is required." });

        var check = await consentService.CheckConsentAsync(email, channel, topic, ct);
        return Ok(check);
    }

    [Authorize]
    [RequiresFeature("unsubscribe-center")]
    [HttpGet("records")]
    public async Task<ActionResult<IReadOnlyList<GdprConsentRecordDto>>> ListConsentRecords(
        [FromQuery] string? email,
        [FromQuery] CommunicationChannel? channel,
        [FromQuery] string? topic,
        [FromQuery] ConsentStatus? status,
        CancellationToken ct)
    {
        var list = await consentService.ListConsentRecordsAsync(email, channel, topic, status, ct);
        return Ok(list);
    }

    [Authorize]
    [RequiresFeature("unsubscribe-center")]
    [HttpPost("records")]
    public async Task<ActionResult<GdprConsentRecordDto>> SaveConsentRecord(
        [FromBody] SaveConsentRecordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = HttpContext.Request.Headers.UserAgent.ToString();

        var created = await consentService.SaveConsentRecordAsync(request, ip, ua, ct);
        return Ok(created);
    }

    [Authorize]
    [RequiresFeature("unsubscribe-center")]
    [HttpPost("export")]
    public async Task<ActionResult<GdprSubjectDataExport>> ExportSubjectData(
        [FromBody] GeneratePreferenceTokenRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required." });

        var data = await consentService.ExportSubjectDataAsync(request.Email, ct);
        return Ok(data);
    }

    [Authorize]
    [RequiresFeature("unsubscribe-center")]
    [HttpPost("erasure")]
    public async Task<ActionResult<GdprErasureResult>> ExecuteErasure(
        [FromBody] GdprErasureRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required." });

        var result = await consentService.ExecuteErasureAsync(request, ct);
        return Ok(result);
    }
}
