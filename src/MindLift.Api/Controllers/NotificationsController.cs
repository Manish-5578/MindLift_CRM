using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Infrastructure.Notifications;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>A person's own notifications. Sign-in only: everything is filtered to the signed-in person inside the service.</summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public sealed class NotificationsController(NotificationQueryService notifications) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationPageDto>> List([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(await notifications.ListAsync(unreadOnly, page, pageSize, cancellationToken));

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> Unread(CancellationToken cancellationToken)
        => Ok(await notifications.UnreadAsync(cancellationToken));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await notifications.MarkReadAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await notifications.MarkAllReadAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<NotificationPreferencesDto>> Preferences(CancellationToken cancellationToken)
        => Ok(await notifications.GetPreferencesAsync(cancellationToken));

    [HttpPut("preferences")]
    public async Task<ActionResult<NotificationPreferencesDto>> SavePreferences([FromBody] SaveNotificationPreferencesRequest request, CancellationToken cancellationToken)
        => Ok(await notifications.SavePreferencesAsync(request, cancellationToken));
}
