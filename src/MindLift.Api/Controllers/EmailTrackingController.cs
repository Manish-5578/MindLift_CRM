using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 5 / #30: Email open and click tracking (email-tracking).
/// Endpoints for email HTML transformation, open pixel tracking, link click redirect proxy,
/// and email engagement analytics.
/// </summary>
[ApiController]
[Route("api/v1/email-tracking")]
public sealed class EmailTrackingController(IEmailTrackingService trackingService) : ControllerBase
{
    // ==========================================
    // Public / Anonymous Tracking Endpoints
    // ==========================================

    [AllowAnonymous]
    [HttpGet("pixel/{token}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TrackOpen(string token, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var gif = await trackingService.RecordOpenAsync(token, ip, userAgent, ct);
        if (gif == null) return NotFound();

        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        return File(gif, "image/gif");
    }

    [AllowAnonymous]
    [HttpGet("click/{token}")]
    public async Task<IActionResult> TrackClick(string token, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var originalUrl = await trackingService.RecordClickAsync(token, ip, userAgent, ct);
        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            return NotFound("Link not found or expired.");
        }

        return Redirect(originalUrl);
    }

    // ==========================================
    // Authenticated Management Endpoints
    // ==========================================

    [Authorize]
    [RequiresFeature("email-tracking")]
    [HttpPost("register")]
    public async Task<ActionResult<RegisterTrackedEmailResult>> RegisterAndTransform(
        [FromBody] RegisterTrackedEmailRequest request,
        CancellationToken ct)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await trackingService.RegisterAndTransformEmailAsync(request, baseUrl, ct);
        return Ok(result);
    }

    [Authorize]
    [RequiresFeature("email-tracking")]
    [HttpGet("emails")]
    public async Task<ActionResult<IReadOnlyList<TrackedEmailDto>>> ListEmails(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var emails = await trackingService.ListTrackedEmailsAsync(entityType, entityId, page, pageSize, ct);
        return Ok(emails);
    }

    [Authorize]
    [RequiresFeature("email-tracking")]
    [HttpGet("emails/{id:guid}")]
    public async Task<ActionResult<TrackedEmailDto>> GetEmail(Guid id, CancellationToken ct)
    {
        var email = await trackingService.GetTrackedEmailAsync(id, ct);
        if (email == null) return NotFound();
        return Ok(email);
    }

    [Authorize]
    [RequiresFeature("email-tracking")]
    [HttpGet("emails/{id:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<TrackedEmailEventDto>>> ListEvents(Guid id, CancellationToken ct)
    {
        var events = await trackingService.ListEventsAsync(id, ct);
        return Ok(events);
    }

    [Authorize]
    [RequiresFeature("email-tracking")]
    [HttpGet("settings")]
    public async Task<ActionResult<EmailTrackingSettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await trackingService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [Authorize]
    [RequiresFeature("email-tracking")]
    [HttpPut("settings")]
    public async Task<ActionResult<EmailTrackingSettingsDto>> UpdateSettings(
        [FromBody] SaveEmailTrackingSettingsRequest request,
        CancellationToken ct)
    {
        var updated = await trackingService.UpdateSettingsAsync(request, ct);
        return Ok(updated);
    }

    [Authorize]
    [RequiresFeature("email-tracking")]
    [HttpGet("stats")]
    public async Task<ActionResult<EmailTrackingStatsDto>> GetStats(CancellationToken ct)
    {
        var stats = await trackingService.GetStatsAsync(ct);
        return Ok(stats);
    }
}
