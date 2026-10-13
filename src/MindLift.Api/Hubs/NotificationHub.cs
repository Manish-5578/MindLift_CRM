using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Hubs;

/// <summary>
/// Real-time channel for the staff app (plan 4.12). A connection must be signed in. It joins a group for the person and one for the workspace,
/// so a message for one person never reaches another. The browser sends its token as access_token because WebSockets cannot set headers.
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (Guid.TryParse(user?.FindFirst("sub")?.Value, out var userId) && Guid.TryParse(user?.FindFirst("tid")?.Value, out var tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>Sends a new notification to the person's open tabs.</summary>
public sealed class SignalRNotificationPusher(IHubContext<NotificationHub> hub) : INotificationPusher
{
    public Task PushAsync(Guid tenantId, Guid userId, NotificationDto notification, int unread, CancellationToken cancellationToken = default)
        => hub.Clients.Group(NotificationHub.UserGroup(userId)).SendAsync("ReceiveNotification", notification, unread, cancellationToken);
}
