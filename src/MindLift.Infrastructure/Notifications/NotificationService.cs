using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Notifications;

/// <summary>
/// The notification pipeline (plan 7.11): pick the recipients, apply each person's preferences, save the in-app notification,
/// push it to their open tabs, and queue one email per burst.
/// </summary>
public sealed class NotificationService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    INotificationPusher pusher,
    IBackgroundJobClient jobs,
    ILogger<NotificationService> logger) : INotificationService
{
    private static readonly TimeSpan EmailGroupWindow = TimeSpan.FromMinutes(10);

    public async Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        if (!NotificationTypes.IsKnown(request.Type))
        {
            throw new ArgumentException($"Unknown notification type {request.Type}.", nameof(request));
        }

        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No tenant is set for this operation.");
        var recipients = request.UserIds.Distinct().Where(id => request.IncludeActor || id != tenant.UserId).ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        var active = await db.Users.AsNoTracking().Where(u => recipients.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken);
        if (active.Count == 0)
        {
            return;
        }

        var preferences = await db.NotificationPreferences.AsNoTracking().Where(p => active.Contains(p.UserId) && p.Type == request.Type).ToDictionaryAsync(p => p.UserId, cancellationToken);
        var emailByDefault = NotificationTypes.All.First(t => t.Type == request.Type).EmailByDefault;
        var now = clock.UtcNow;
        var created = new List<Notification>();
        var emails = new List<Guid>();

        foreach (var userId in active)
        {
            preferences.TryGetValue(userId, out var preference);
            var inApp = preference?.InApp ?? true;
            var email = preference?.Email ?? emailByDefault;

            var notification = new Notification
            {
                TenantId = tenantId,
                UserId = userId,
                Type = request.Type,
                Title = Truncate(request.Title, 250),
                Body = request.Body is null ? null : Truncate(request.Body, 2000),
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                Url = request.Url,
                GroupKey = request.GroupKey,
            };

            if (email && !request.InAppOnly && await EmailAllowedAsync(userId, request.GroupKey, now, cancellationToken))
            {
                notification.EmailedAt = now;
                emails.Add(userId);
            }

            // A person who turned the in-app copy off still gets the email, but the notification is kept read so it never shows in the bell.
            if (!inApp)
            {
                notification.IsRead = true;
                notification.ReadAt = now;
            }

            created.Add(notification);
            db.Notifications.Add(notification);
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var notification in created.Where(n => !n.IsRead))
        {
            await PushAsync(tenantId, notification, cancellationToken);
        }

        foreach (var userId in emails)
        {
            var notification = created.First(n => n.UserId == userId);
            jobs.Enqueue<NotificationEmailJob>(job => job.SendAsync(tenantId, userId, notification.Title, notification.Body, notification.Url, CancellationToken.None));
        }
    }

    /// <summary>One email per group within ten minutes: a burst of the same kind of event does not fill an inbox.</summary>
    private async Task<bool> EmailAllowedAsync(Guid userId, string? groupKey, DateTime now, CancellationToken cancellationToken)
    {
        if (groupKey is null)
        {
            return true;
        }

        var since = now - EmailGroupWindow;
        return !await db.Notifications.AnyAsync(n => n.UserId == userId && n.GroupKey == groupKey && n.EmailedAt != null && n.EmailedAt >= since, cancellationToken);
    }

    private async Task PushAsync(Guid tenantId, Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            var unread = await db.Notifications.CountAsync(n => n.UserId == notification.UserId && !n.IsRead, cancellationToken);
            await pusher.PushAsync(tenantId, notification.UserId, NotificationMapper.ToDto(notification), unread, cancellationToken);
        }
        catch (Exception ex)
        {
            // Real-time delivery is a bonus: the notification is saved and shows on the next refresh.
            logger.LogWarning(ex, "Could not push notification {NotificationId} to user {UserId}", notification.Id, notification.UserId);
        }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}

public static class NotificationMapper
{
    public static NotificationDto ToDto(Notification n)
        => new(n.Id, n.Type, n.Title, n.Body, n.EntityType, n.EntityId, n.Url, n.IsRead, n.CreatedAt == default ? DateTime.UtcNow : n.CreatedAt);
}

/// <summary>Sends the email copy of a notification. Runs in the background so a slow mail server never slows a request.</summary>
public sealed class NotificationEmailJob(AppDbContext db, IEmailSender email, IOptions<AppOptions> app)
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [1, 2, 5])]
    public async Task SendAsync(Guid tenantId, Guid userId, string title, string? body, string? url, CancellationToken cancellationToken)
    {
        // A notification queued before the workspace lapsed is not emailed after it.
        var workspace = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => new { t.Status, t.TrialEndsAt }).FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || MindLift.Infrastructure.Tenancy.TenantAccess.IsLapsed(workspace.Status, workspace.TrialEndsAt, DateTime.UtcNow))
        {
            return;
        }

        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId && u.IsActive, cancellationToken);
        if (user?.Email is null)
        {
            return;
        }

        var link = url is null ? string.Empty : $"\n\nOpen it: {app.Value.BaseUrl}{url}";
        await email.SendAsync(
            user.Email,
            title,
            $"Hello {user.FirstName},\n\n{title}{(string.IsNullOrWhiteSpace(body) ? string.Empty : "\n" + body)}{link}\n\nYou can change which emails you get under Notification preferences in MindLift.",
            cancellationToken);
    }
}
