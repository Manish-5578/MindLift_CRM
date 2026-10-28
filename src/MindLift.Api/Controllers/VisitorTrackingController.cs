using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 4 / #25: Website visitor tracking (visitor-tracking).
/// Endpoints for web tracker management, visitor analytics, and public hit tracking.
/// </summary>
[ApiController]
[Route("api/v1/visitor-tracking")]
public sealed class VisitorTrackingController(IVisitorTrackingService trackingService) : ControllerBase
{
    // ================= Authenticated Tracker Management =================

    [Authorize]
    [RequiresFeature("visitor-tracking")]
    [HttpPost("trackers")]
    public async Task<ActionResult<WebsiteTrackerDto>> CreateTracker(
        [FromBody] SaveWebsiteTrackerRequest request,
        CancellationToken ct)
    {
        var created = await trackingService.CreateTrackerAsync(request, ct);
        return CreatedAtAction(nameof(GetTrackerById), new { id = created.Id }, created);
    }

    [Authorize]
    [RequiresFeature("visitor-tracking")]
    [HttpGet("trackers")]
    public async Task<ActionResult<IReadOnlyList<WebsiteTrackerDto>>> ListTrackers(CancellationToken ct)
    {
        var list = await trackingService.ListTrackersAsync(ct);
        return Ok(list);
    }

    [Authorize]
    [RequiresFeature("visitor-tracking")]
    [HttpGet("trackers/{id:guid}")]
    public async Task<ActionResult<WebsiteTrackerDto>> GetTrackerById(
        Guid id,
        CancellationToken ct)
    {
        var tracker = await trackingService.GetTrackerByIdAsync(id, ct);
        return Ok(tracker);
    }

    [Authorize]
    [RequiresFeature("visitor-tracking")]
    [HttpPut("trackers/{id:guid}")]
    public async Task<ActionResult<WebsiteTrackerDto>> UpdateTracker(
        Guid id,
        [FromBody] SaveWebsiteTrackerRequest request,
        CancellationToken ct)
    {
        var updated = await trackingService.UpdateTrackerAsync(id, request, ct);
        return Ok(updated);
    }

    [Authorize]
    [RequiresFeature("visitor-tracking")]
    [HttpDelete("trackers/{id:guid}")]
    public async Task<IActionResult> DeleteTracker(
        Guid id,
        CancellationToken ct)
    {
        var deleted = await trackingService.DeleteTrackerAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [Authorize]
    [RequiresFeature("visitor-tracking")]
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<VisitorSessionDto>>> GetSessions(
        [FromQuery] Guid? trackerId,
        [FromQuery] Guid? leadId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var sessions = await trackingService.GetVisitorSessionsAsync(trackerId, leadId, limit, ct);
        return Ok(sessions);
    }

    [Authorize]
    [RequiresFeature("visitor-tracking")]
    [HttpGet("analytics")]
    public async Task<ActionResult<VisitorAnalyticsDto>> GetAnalytics(
        [FromQuery] Guid? trackerId,
        CancellationToken ct)
    {
        var summary = await trackingService.GetAnalyticsSummaryAsync(trackerId, ct);
        return Ok(summary);
    }

    // ================= Public Tracking Ingestion =================

    [AllowAnonymous]
    [HttpPost("hit")]
    public async Task<ActionResult<TrackHitResultDto>> TrackHit(
        [FromBody] TrackHitRequest request,
        CancellationToken ct)
    {
        var result = await trackingService.TrackHitAsync(request, ct);
        if (!result.Success && result.Message.Contains("Invalid"))
            return BadRequest(result);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("identify")]
    public async Task<ActionResult<TrackHitResultDto>> Identify(
        [FromBody] IdentifyVisitorRequest request,
        CancellationToken ct)
    {
        var result = await trackingService.IdentifyVisitorAsync(request, ct);
        if (!result.Success && result.Message.Contains("Invalid"))
            return BadRequest(result);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("tracker.js")]
    public IActionResult GetTrackerScript()
    {
        var js = """
            (function() {
                var script = document.currentScript;
                var token = script ? script.getAttribute('data-tracker-token') : null;
                if (!token) return;
                var vid = localStorage.getItem('ml_vid') || ('v_' + Math.random().toString(36).substring(2) + Date.now().toString(36));
                localStorage.setItem('ml_vid', vid);
                var sid = sessionStorage.getItem('ml_sid') || ('s_' + Math.random().toString(36).substring(2) + Date.now().toString(36));
                sessionStorage.setItem('ml_sid', sid);
                fetch('/api/v1/visitor-tracking/hit', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        trackingToken: token,
                        visitorId: vid,
                        sessionId: sid,
                        url: window.location.href,
                        title: document.title,
                        referrer: document.referrer
                    })
                }).catch(function(){});
            })();
            """;
        return Content(js, "application/javascript");
    }
}
