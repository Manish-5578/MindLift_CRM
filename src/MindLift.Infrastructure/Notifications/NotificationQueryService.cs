using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Notifications;

/// <summary>A person's own notifications and preferences. Nothing here ever shows another person's.</summary>
public sealed class NotificationQueryService(AppDbContext db, ITenantContext tenant, IClock clock)
{
    private Guid Me => tenant.UserId ?? throw new UnauthorizedException("not_signed_in", "Please sign in.");

    public async Task<NotificationPageDto> ListAsync(bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var me = Me;
        var source = db.Notifications.AsNoTracking().Where(n => n.UserId == me && (!unreadOnly || !n.IsRead));
        var total = await source.CountAsync(cancellationToken);
        var rows = await source.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var unread = await db.Notifications.CountAsync(n => n.UserId == me && !n.IsRead, cancellationToken);
        return new NotificationPageDto(rows.Select(NotificationMapper.ToDto).ToList(), total, unread, page, pageSize);
    }

    public async Task<UnreadCountDto> UnreadAsync(CancellationToken cancellationToken = default)
    {
        var me = Me;
        return new UnreadCountDto(await db.Notifications.CountAsync(n => n.UserId == me && !n.IsRead, cancellationToken));
    }

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var me = Me;
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == me, cancellationToken) ?? throw new NotFoundException("Notification", id);
        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var me = Me;
        var now = clock.UtcNow;
        await db.Notifications.Where(n => n.UserId == me && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    public async Task<NotificationPreferencesDto> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        var me = Me;
        var saved = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == me).ToDictionaryAsync(p => p.Type, cancellationToken);
        var rows = NotificationTypes.All.Select(t =>
            saved.TryGetValue(t.Type, out var p)
                ? new NotificationPreferenceDto(t.Type, t.Label, p.InApp, p.Email)
                : new NotificationPreferenceDto(t.Type, t.Label, true, t.EmailByDefault)).ToList();
        return new NotificationPreferencesDto(rows, saved.TryGetValue(NotificationTypes.DailyDigest, out var digest) && digest.Email);
    }

    public async Task<NotificationPreferencesDto> SavePreferencesAsync(SaveNotificationPreferencesRequest request, CancellationToken cancellationToken = default)
    {
        var me = Me;
        var unknown = request.Preferences.FirstOrDefault(p => !NotificationTypes.All.Any(t => t.Type == p.Type));
        if (unknown is not null)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["preferences"] = [$"{unknown.Type} is not a kind of notification."] });
        }

        var existing = await db.NotificationPreferences.Where(p => p.UserId == me).ToListAsync(cancellationToken);
        foreach (var wanted in request.Preferences.GroupBy(p => p.Type).Select(g => g.Last()))
        {
            var row = existing.FirstOrDefault(p => p.Type == wanted.Type);
            if (row is null)
            {
                row = new NotificationPreference { UserId = me, Type = wanted.Type };
                db.NotificationPreferences.Add(row);
                existing.Add(row);
            }

            row.InApp = wanted.InApp;
            row.Email = wanted.Email;
        }

        var digest = existing.FirstOrDefault(p => p.Type == NotificationTypes.DailyDigest);
        if (digest is null)
        {
            digest = new NotificationPreference { UserId = me, Type = NotificationTypes.DailyDigest, InApp = false };
            db.NotificationPreferences.Add(digest);
        }

        digest.Email = request.DailyDigest;
        await db.SaveChangesAsync(cancellationToken);
        return await GetPreferencesAsync(cancellationToken);
    }
}
