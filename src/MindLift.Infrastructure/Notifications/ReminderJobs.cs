using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Notifications;

/// <summary>Runs one batch of work for each workspace that has some, with that workspace set as the current one.</summary>
internal static class TenantBatch
{
    public static async Task ForEachTenantAsync<T>(IEnumerable<T> rows, Func<T, Guid> tenantOf, ITenantContext tenant, Func<Guid, List<T>, Task> work)
    {
        foreach (var group in rows.GroupBy(tenantOf))
        {
            using (tenant.Use(group.Key))
            {
                await work(group.Key, group.ToList());
            }
        }
    }
}

/// <summary>Every minute: tasks and meetings whose "remind me" time has come get one reminder (plan 7.11).</summary>
public sealed class ActivityReminderJob(AppDbContext db, ITenantContext tenant, INotificationService notifications, IClock clock)
{
    private const int BatchSize = 500;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        // A reminder is set at most 30 days ahead, so nothing due later than that can be ready. The exact minute is checked in memory.
        var candidates = await db.Activities.IgnoreQueryFilters()
            .Where(a => !a.IsDeleted && a.Status == "Open" && a.DueAt != null && a.ReminderMinutes != null && a.ReminderSentAt == null
                && (a.Type == "Task" || a.Type == "Meeting") && a.DueAt <= now.AddDays(31))
            .OrderBy(a => a.DueAt).Take(BatchSize * 4).ToListAsync(cancellationToken);
        var due = candidates.Where(a => a.DueAt!.Value.AddMinutes(-a.ReminderMinutes!.Value) <= now).Take(BatchSize).ToList();

        await TenantBatch.ForEachTenantAsync(due, a => a.TenantId, tenant, async (_, rows) =>
        {
            foreach (var activity in rows)
            {
                var meeting = activity.Type == "Meeting";
                var when = activity.DueAt!.Value;
                var late = when < now;
                await notifications.NotifyAsync(new NotificationRequest(
                    [activity.OwnerId],
                    meeting ? NotificationTypes.MeetingReminder : NotificationTypes.TaskDueSoon,
                    meeting ? $"Meeting soon: {activity.Subject}" : $"Task due soon: {activity.Subject}",
                    late ? "It was due a moment ago." : $"Due {when:HH:mm} UTC on {when:d MMM}.",
                    "activities", activity.Id, ActivityUrl(activity), $"reminder:{activity.Id}", IncludeActor: true), CancellationToken.None);

                // The activity was read without the tenant filter, so it is tracked by this context and saved with the others below.
                var tracked = await db.Activities.FirstAsync(a => a.Id == activity.Id, cancellationToken);
                tracked.ReminderSentAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
        });

        return due.Count;
    }

    internal static string ActivityUrl(Activity a)
        => a.DealId is { } d ? $"/deals/{d}" : a.ContactId is { } c ? $"/contacts/{c}" : a.AccountId is { } ac ? $"/accounts/{ac}" : a.LeadId is { } l ? $"/leads/{l}" : "/activities";
}

/// <summary>Every 15 minutes: a task that is past due and still open tells its owner once.</summary>
public sealed class OverdueTaskJob(AppDbContext db, ITenantContext tenant, INotificationService notifications, IClock clock)
{
    private const int BatchSize = 500;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var overdue = await db.Activities.IgnoreQueryFilters().AsNoTracking()
            .Where(a => !a.IsDeleted && a.Type == "Task" && a.Status == "Open" && a.DueAt != null && a.DueAt < now && a.OverdueNotifiedAt == null)
            .OrderBy(a => a.DueAt).Take(BatchSize).ToListAsync(cancellationToken);

        await TenantBatch.ForEachTenantAsync(overdue, a => a.TenantId, tenant, async (_, rows) =>
        {
            foreach (var activity in rows)
            {
                await notifications.NotifyAsync(new NotificationRequest(
                    [activity.OwnerId], NotificationTypes.TaskOverdue, $"Task overdue: {activity.Subject}",
                    $"It was due {activity.DueAt:d MMM yyyy HH:mm} UTC.", "activities", activity.Id, ActivityReminderJob.ActivityUrl(activity),
                    $"overdue:{activity.OwnerId}", IncludeActor: true), CancellationToken.None);

                var tracked = await db.Activities.FirstAsync(a => a.Id == activity.Id, cancellationToken);
                tracked.OverdueNotifiedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
        });

        return overdue.Count;
    }
}

/// <summary>
/// Hourly: people who asked for it get a "my day" email at 08:00 in the company time zone, once a day, only when there is something in it.
/// </summary>
public sealed class DailyDigestJob(AppDbContext db, ITenantContext tenant, IEmailSender email, IClock clock, IOptions<AppOptions> app)
{
    public const int SendHour = 8;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var wanted = await db.NotificationPreferences.IgnoreQueryFilters().AsNoTracking()
            .Where(p => !p.IsDeleted && p.Type == NotificationTypes.DailyDigest && p.Email).ToListAsync(cancellationToken);

        var sent = 0;
        await TenantBatch.ForEachTenantAsync(wanted, p => p.TenantId, tenant, async (_, rows) =>
        {
            var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
            var local = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
            if (local.Hour != SendHour)
            {
                return;
            }

            var dayStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), zone);
            var dayEnd = dayStart.AddDays(1);

            foreach (var preference in rows.Where(r => r.LastSentAt is null || r.LastSentAt < dayStart))
            {
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == preference.UserId && u.IsActive, cancellationToken);
                if (user?.Email is null)
                {
                    continue;
                }

                var open = db.Activities.AsNoTracking().Where(a => a.OwnerId == user.Id && a.Status == "Open" && a.DueAt != null);
                var overdue = await open.Where(a => a.DueAt < dayStart).OrderBy(a => a.DueAt).Take(20).ToListAsync(cancellationToken);
                var today = await open.Where(a => a.DueAt >= dayStart && a.DueAt < dayEnd).OrderBy(a => a.DueAt).Take(30).ToListAsync(cancellationToken);
                if (overdue.Count == 0 && today.Count == 0)
                {
                    continue;
                }

                var lines = new List<string> { $"Hello {user.FirstName},", string.Empty, $"Here is your day, {local:dddd d MMMM}." };
                if (overdue.Count > 0)
                {
                    lines.Add(string.Empty);
                    lines.Add($"Overdue ({overdue.Count}):");
                    lines.AddRange(overdue.Select(a => $" - {a.Subject} (was due {a.DueAt:d MMM})"));
                }

                if (today.Count > 0)
                {
                    lines.Add(string.Empty);
                    lines.Add($"Today ({today.Count}):");
                    lines.AddRange(today.Select(a => $" - {a.Type}: {a.Subject} ({TimeZoneInfo.ConvertTimeFromUtc(a.DueAt!.Value, zone):HH:mm})"));
                }

                lines.Add(string.Empty);
                lines.Add($"Open MindLift: {app.Value.BaseUrl}/activities");
                await email.SendAsync(user.Email, $"Your day: {today.Count} today, {overdue.Count} overdue", string.Join("\n", lines), cancellationToken);

                var row = await db.NotificationPreferences.FirstAsync(p => p.Id == preference.Id, cancellationToken);
                row.LastSentAt = now;
                sent++;
            }

            await db.SaveChangesAsync(cancellationToken);
        });

        return sent;
    }
}

/// <summary>Daily: notifications older than 180 days are removed (plan 11, CleanupJob).</summary>
public sealed class NotificationCleanupJob(AppDbContext db, IClock clock)
{
    public const int KeepDays = 180;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.UtcNow.AddDays(-KeepDays);
        return await db.Notifications.IgnoreQueryFilters().Where(n => n.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
