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
    [HttpGet("manage/{token}")]
    public async Task<ActionResult<ManageBookingDto>> Manage(string token, CancellationToken cancellationToken)
        => Ok(await booking.ManageAsync(token, cancellationToken));

    [AllowAnonymous]
    [HttpPost("manage/{token}/cancel")]
    public async Task<IActionResult> Cancel(string token, [FromBody] CancelBookingRequest request, CancellationToken cancellationToken)
    {
        await booking.CancelAsync(token, request, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("manage/{token}/reschedule")]
    public async Task<ActionResult<BookingConfirmationDto>> Reschedule(string token, [FromBody] RescheduleBookingRequest request, CancellationToken cancellationToken)
        => Ok(await booking.RescheduleAsync(token, request, cancellationToken));
}
