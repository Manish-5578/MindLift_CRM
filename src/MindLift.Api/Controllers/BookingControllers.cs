using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Bookings;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>The calendar. Sign-in only; the service applies each person's data scope.</summary>
[ApiController]
[Route("api/v1/calendar")]
[Authorize]
public sealed class CalendarController(CalendarService calendar) : ControllerBase
{
    /// <summary>Meetings, open tasks with a time and bookings between two instants. Times are UTC.</summary>
    [HttpGet("events")]
    public async Task<ActionResult<IReadOnlyList<CalendarEventDto>>> Events(
        [FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] Guid[]? userIds, [FromQuery] Guid? teamId, [FromQuery] string[]? types, CancellationToken cancellationToken)
        => Ok(await calendar.EventsAsync(from, to, userIds, teamId, types, cancellationToken));
}

/// <summary>Booking page settings (Settings, Admin by default).</summary>
[ApiController]
[Route("api/v1/settings/booking-pages")]
public sealed class BookingPagesController(BookingPageService pages) : ControllerBase
{
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookingPageDto>>> List(CancellationToken cancellationToken)
        => Ok(await pages.ListAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingPageDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await pages.GetAsync(id, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("{id:guid}/bookings")]
    public async Task<ActionResult<IReadOnlyList<BookingListItemDto>>> Bookings(Guid id, CancellationToken cancellationToken)
        => Ok(await pages.BookingsAsync(id, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<BookingPageDto>> Create([FromBody] SaveBookingPageRequest request, CancellationToken cancellationToken)
        => Ok(await pages.SaveAsync(null, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BookingPageDto>> Update(Guid id, [FromBody] SaveBookingPageRequest request, CancellationToken cancellationToken)
        => Ok(await pages.SaveAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await pages.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>Public booking pages and the manage link. Anonymous: the workspace and page come from the address, and the manage link is secret.</summary>
[ApiController]
[Route("api/v1/public/booking")]
[AllowAnonymous]
[EnableRateLimiting("public")]
public sealed class PublicBookingController(PublicBookingService booking) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("{tenantSlug}/{pageSlug}")]
    public async Task<ActionResult<PublicBookingPageDto>> Page(string tenantSlug, string pageSlug, CancellationToken cancellationToken)
        => Ok(await booking.PageAsync(tenantSlug, pageSlug, cancellationToken));

    [AllowAnonymous]
    [HttpGet("{tenantSlug}/{pageSlug}/slots")]
    public async Task<ActionResult<IReadOnlyList<SlotDto>>> Slots(string tenantSlug, string pageSlug, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string? tz, CancellationToken cancellationToken)
        => Ok(await booking.SlotsAsync(tenantSlug, pageSlug, from, to, tz, cancellationToken));

    [AllowAnonymous]
    [HttpPost("{tenantSlug}/{pageSlug}/book")]
    public async Task<ActionResult<BookingConfirmationDto>> Book(string tenantSlug, string pageSlug, [FromBody] BookRequest request, CancellationToken cancellationToken)
        => Ok(await booking.BookAsync(tenantSlug, pageSlug, request, cancellationToken));

    [AllowAnonymous]
    [HttpGet("manage")]
    [HttpGet("manage/{token}")]
    public async Task<ActionResult<ManageBookingDto>> Manage([FromRoute] string? token, CancellationToken cancellationToken)
        => Ok(await booking.ManageAsync(ExtractToken(token), cancellationToken));

    [AllowAnonymous]
    [HttpPost("manage/cancel")]
    [HttpPost("manage/{token}/cancel")]
    public async Task<IActionResult> Cancel([FromRoute] string? token, [FromBody] CancelBookingRequest request, CancellationToken cancellationToken)
    {
        var resolvedToken = ExtractToken(token, request.Token);
        await booking.CancelAsync(resolvedToken, request, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("manage/reschedule")]
    [HttpPost("manage/{token}/reschedule")]
    public async Task<ActionResult<BookingConfirmationDto>> Reschedule([FromRoute] string? token, [FromBody] RescheduleBookingRequest request, CancellationToken cancellationToken)
    {
        var resolvedToken = ExtractToken(token, request.Token);
        return Ok(await booking.RescheduleAsync(resolvedToken, request, cancellationToken));
    }

    private string ExtractToken(string? routeToken, string? bodyToken = null)
    {
        if (!string.IsNullOrWhiteSpace(bodyToken)) return bodyToken.Trim();
        if (Request.Headers.TryGetValue("X-Booking-Token", out var headerVal) && !string.IsNullOrWhiteSpace(headerVal))
            return headerVal.ToString().Trim();
        if (Request.Headers.TryGetValue("Authorization", out var authVal) && !string.IsNullOrWhiteSpace(authVal))
        {
            var str = authVal.ToString().Trim();
            if (str.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return str["Bearer ".Length..].Trim();
        }
        if (!string.IsNullOrWhiteSpace(routeToken)) return routeToken.Trim();

        throw new MindLift.Application.Common.Exceptions.UnauthorizedException("missing_booking_token", "Booking management token is required.");
    }
}
