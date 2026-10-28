using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 5 / #36: Meeting booking links (meeting-links).
/// REST endpoints for personalized meeting links, custom duration & location,
/// pre-filled customer details, link analytics, and public self-scheduling.
/// </summary>
[ApiController]
[Route("api/v1/meeting-links")]
public sealed class MeetingLinksController(
    IMeetingLinkService linkService,
    ITenantContext tenantContext) : ControllerBase
{
    private (Guid UserId, string UserName) CurrentUser()
    {
        var id = tenantContext.UserId ?? Guid.Empty;
        if (id == Guid.Empty)
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (Guid.TryParse(sub, out var parsed)) id = parsed;
        }

        if (id == Guid.Empty)
            throw new UnauthorizedException("not_signed_in", "User is not signed in.");

        var name = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("name") ?? "Host";
        return (id, name);
    }

    // ==========================================
    // Public Anonymous Endpoints
    // ==========================================

    [AllowAnonymous]
    [HttpGet("public/{slug}")]
    public async Task<ActionResult<PublicMeetingLinkDto>> GetPublicMeetingLink(
        string slug,
        CancellationToken ct)
    {
        var link = await linkService.GetPublicMeetingLinkAsync(slug, ct);
        return Ok(link);
    }

    [AllowAnonymous]
    [HttpPost("public/{slug}/track-click")]
    public async Task<IActionResult> TrackClick(
        string slug,
        CancellationToken ct)
    {
        await linkService.TrackClickAsync(slug, ct);
        return Ok(new { success = true });
    }

    [AllowAnonymous]
    [HttpPost("public/{slug}/book")]
    public async Task<ActionResult<BookingConfirmationDto>> BookThroughLink(
        string slug,
        [FromBody] BookThroughMeetingLinkRequest request,
        CancellationToken ct)
    {
        var confirmation = await linkService.BookThroughLinkAsync(slug, request, ct);
        return Ok(confirmation);
    }

    // ==========================================
    // Authenticated Endpoints (RequiresFeature "meeting-links")
    // ==========================================

    [Authorize]
    [RequiresFeature("meeting-links")]
    [HttpPost]
    public async Task<ActionResult<MeetingLinkDto>> CreateMeetingLink(
        [FromBody] CreateMeetingLinkRequest request,
        CancellationToken ct)
    {
        var (userId, userName) = CurrentUser();
        var link = await linkService.CreateMeetingLinkAsync(request, userId, userName, ct);
        return CreatedAtAction(nameof(GetMeetingLink), new { id = link.Id }, link);
    }

    [Authorize]
    [RequiresFeature("meeting-links")]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MeetingLinkDto>>> GetMeetingLinks(
        [FromQuery] Guid? userId = null,
        [FromQuery] Guid? dealId = null,
        [FromQuery] Guid? contactId = null,
        [FromQuery] Guid? leadId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var query = new MeetingLinkQuery(userId, dealId, contactId, leadId, isActive, limit);
        var links = await linkService.GetMeetingLinksAsync(query, ct);
        return Ok(links);
    }

    [Authorize]
    [RequiresFeature("meeting-links")]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MeetingLinkDto>> GetMeetingLink(
        Guid id,
        CancellationToken ct)
    {
        var link = await linkService.GetMeetingLinkAsync(id, ct);
        return Ok(link);
    }

    [Authorize]
    [RequiresFeature("meeting-links")]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MeetingLinkDto>> UpdateMeetingLink(
        Guid id,
        [FromBody] UpdateMeetingLinkRequest request,
        CancellationToken ct)
    {
        var updated = await linkService.UpdateMeetingLinkAsync(id, request, ct);
        return Ok(updated);
    }

    [Authorize]
    [RequiresFeature("meeting-links")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteMeetingLink(
        Guid id,
        CancellationToken ct)
    {
        await linkService.DeleteMeetingLinkAsync(id, ct);
        return NoContent();
    }
}
