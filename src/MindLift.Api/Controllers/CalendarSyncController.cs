using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Infrastructure.CalendarSync;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>A person connecting their own Google or Microsoft calendar, and their own calendar feed. Everyone signed in may do this for themselves.</summary>
[ApiController]
[Route("api/v1/calendar/sync")]
[Authorize]
public sealed class CalendarSyncController(CalendarSyncService sync) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CalendarSyncDto>> Status(CancellationToken cancellationToken) => Ok(await sync.StatusAsync(cancellationToken));

    [HttpPost("connect/{provider}")]
    public ActionResult<CalendarConnectUrlDto> Connect(string provider) => Ok(new CalendarConnectUrlDto(sync.ConnectUrl(provider)));

    [HttpPost("now")]
    public async Task<ActionResult<CalendarSyncDto>> SyncNow(CancellationToken cancellationToken)
    {
        await sync.SyncMineAsync(cancellationToken);
        return Ok(await sync.StatusAsync(cancellationToken));
    }

    [HttpDelete("connections/{id:guid}")]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken cancellationToken)
    {
        await sync.DisconnectAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Makes (or replaces) the feed address. The address is returned this once.</summary>
    [HttpPost("feed")]
    public async Task<ActionResult<CalendarFeedDto>> NewFeed(CancellationToken cancellationToken) => Ok(new CalendarFeedDto(await sync.NewFeedAsync(cancellationToken)));

    [HttpDelete("feed")]
    public async Task<IActionResult> RemoveFeed(CancellationToken cancellationToken)
    {
        await sync.RemoveFeedAsync(cancellationToken);
        return NoContent();
    }
}

/// <summary>Where the calendar service sends the person back after they agree, and where a calendar app reads the feed. The address carries its own proof.</summary>
[ApiController]
[AllowAnonymous]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("public")]
public sealed class CalendarPublicController(CalendarSyncService sync) : ControllerBase
{
    [HttpGet("/calendar/oauth/{provider}/callback")]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        var ok = error is null && await sync.CompleteAsync(provider, code, state, cancellationToken);
        return Redirect(ok ? "/calendar/connections?connected=" + Uri.EscapeDataString(provider) : "/calendar/connections?failed=" + Uri.EscapeDataString(provider));
    }

    [HttpGet("/calendar/feed/{token}.ics")]
    public async Task<IActionResult> Feed(string token, CancellationToken cancellationToken)
    {
        var ics = await sync.FeedAsync(token, cancellationToken);
        if (ics is null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "private, max-age=300";
        return Content(ics, "text/calendar; charset=utf-8");
    }
}
