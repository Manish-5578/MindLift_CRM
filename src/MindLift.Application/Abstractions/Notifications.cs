using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>What to tell people. The service skips the person who caused it, people who are switched off, and people who turned that type off.</summary>
/// <param name="InAppOnly">True when the caller already sends its own email.</param>
/// <param name="IncludeActor">True for things the actor should still hear about, such as an import they started finishing.</param>
public sealed record NotificationRequest(
    IReadOnlyCollection<Guid> UserIds,
    string Type,
    string Title,
    string? Body = null,
    string? EntityType = null,
    Guid? EntityId = null,
    string? Url = null,
    string? GroupKey = null,
    bool IncludeActor = false,
    bool InAppOnly = false);

/// <summary>Creates notifications, delivers them in the app and by email (plan 7.11).</summary>
public interface INotificationService
{
    Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Sends a new notification to the person's open browser tabs. Implemented where SignalR lives.</summary>
public interface INotificationPusher
{
    Task PushAsync(Guid tenantId, Guid userId, NotificationDto notification, int unread, CancellationToken cancellationToken = default);
}

/// <summary>Used when nothing is listening, for example in tests of other things.</summary>
public sealed class NullNotificationPusher : INotificationPusher
{
    public Task PushAsync(Guid tenantId, Guid userId, NotificationDto notification, int unread, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
