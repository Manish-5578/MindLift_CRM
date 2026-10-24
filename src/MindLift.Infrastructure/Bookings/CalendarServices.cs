using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Contacts;
using MindLift.Infrastructure.Notifications;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Bookings;

/// <summary>Meetings, tasks with a time and bookings in a date range, for one or several people (plan 7.9).</summary>
public sealed class CalendarService(AppDbContext db, ITenantContext tenant, IDataScopeService scope)
{
    private const int MaxRangeDays = 62;
    private const int MaxEvents = 2000;
    private static readonly TimeSpan DefaultMeeting = TimeSpan.FromHours(1);

    /// <summary>
    /// With no people named, the signed-in person's own calendar. Other people (or a team) can only be shown when the person's data scope
    /// for Activities allows it; anyone outside it is quietly left out.
    /// </summary>
    public async Task<IReadOnlyList<CalendarEventDto>> EventsAsync(DateTime from, DateTime to, IReadOnlyList<Guid>? userIds, Guid? teamId, IReadOnlyList<string>? kinds, CancellationToken cancellationToken = default)
    {
        if (to <= from || (to - from).TotalDays > MaxRangeDays)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["to"] = [$"Ask for a range of up to {MaxRangeDays} days, ending after it starts."] });
        }

        var me = tenant.UserId ?? throw new UnauthorizedException("not_signed_in", "Please sign in.");
        var allowed = await scope.GetScopeUserIdsAsync(AppModule.Activities, cancellationToken);

        var wanted = new HashSet<Guid>();
        if (teamId is { } team)
        {
            wanted.UnionWith(await db.Users.AsNoTracking().Where(u => u.TeamId == team && u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken));
        }

        if (userIds is { Count: > 0 })
        {
            wanted.UnionWith(userIds);
        }

        if (wanted.Count == 0)
        {
            wanted.Add(me);
        }

        var people = wanted.Where(id => id == me || allowed is null || allowed.Contains(id)).ToList();
        if (people.Count == 0)
        {
            return [];
        }

        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to, DateTimeKind.Utc);
        var wantMeetings = kinds is null || kinds.Count == 0 || kinds.Contains("meeting", StringComparer.OrdinalIgnoreCase) || kinds.Contains("booking", StringComparer.OrdinalIgnoreCase);
        var wantTasks = kinds is null || kinds.Count == 0 || kinds.Contains("task", StringComparer.OrdinalIgnoreCase);
        var events = new List<CalendarEventDto>();

        var rows = await db.Activities.AsNoTracking()
            .Where(a => people.Contains(a.OwnerId) && a.DueAt != null && a.Status != "Cancelled"
                && ((a.Type == ActivityTypes.Meeting && wantMeetings) || (a.Type == ActivityTypes.Task && wantTasks && a.Status == "Open"))
                && a.DueAt < toUtc && (a.EndAt != null ? a.EndAt > fromUtc : a.DueAt.Value.AddHours(1) > fromUtc))
            .OrderBy(a => a.DueAt).Take(MaxEvents).ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var bookingIds = await db.Bookings.AsNoTracking().Where(b => ids.Contains(b.ActivityId)).Select(b => b.ActivityId).ToListAsync(cancellationToken);
        var owners = await RecordHelpers.OwnerNamesAsync(db, rows.Select(r => r.OwnerId).Distinct(), cancellationToken);

        var contactIds = rows.Where(r => r.ContactId != null).Select(r => r.ContactId!.Value).Distinct().ToList();
        var dealIds = rows.Where(r => r.DealId != null).Select(r => r.DealId!.Value).Distinct().ToList();
        var leadIds = rows.Where(r => r.LeadId != null).Select(r => r.LeadId!.Value).Distinct().ToList();
        var contacts = await db.Contacts.AsNoTracking().Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => (c.FirstName + " " + c.LastName).Trim(), cancellationToken);
        var deals = await db.Deals.AsNoTracking().Where(d => dealIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
        var leadNames = await db.Leads.AsNoTracking().Where(l => leadIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => (l.FirstName + " " + l.LastName).Trim(), cancellationToken);

        foreach (var a in rows)
        {
            var isBooking = bookingIds.Contains(a.Id);
            var kind = isBooking ? "booking" : a.Type == ActivityTypes.Meeting ? "meeting" : "task";
            if (kinds is { Count: > 0 } && !kinds.Contains(kind, StringComparer.OrdinalIgnoreCase) && !(kind == "booking" && kinds.Contains("meeting", StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            var start = a.DueAt!.Value;
            var end = a.Type == ActivityTypes.Task ? start.AddMinutes(30) : a.EndAt ?? start + DefaultMeeting;
            string? linked = a.DealId is { } d && deals.TryGetValue(d, out var dn) ? dn
                : a.ContactId is { } c && contacts.TryGetValue(c, out var cn) ? cn
                : a.LeadId is { } l && leadNames.TryGetValue(l, out var ln) ? ln : null;
            var href = a.DealId is { } deal ? $"/deals/{deal}" : a.ContactId is { } contact ? $"/contacts/{contact}" : a.LeadId is { } lead ? $"/leads/{lead}" : "/activities";
            events.Add(new CalendarEventDto(a.Id, kind, a.Subject, start, end, a.OwnerId, owners.GetValueOrDefault(a.OwnerId), a.Status, href, linked));
        }

        // Times taken in the person's own Google or Microsoft calendar, shown as "Busy" (the event itself is never copied here).
        if (kinds is null || kinds.Count == 0 || kinds.Contains("external", StringComparer.OrdinalIgnoreCase))
        {
            var blocks = await db.ExternalBusyBlocks.AsNoTracking().Where(b => b.UserId == me && b.StartAt < toUtc && b.EndAt > fromUtc).OrderBy(b => b.StartAt).Take(500).ToListAsync(cancellationToken);
            events.AddRange(blocks.Select(b => new CalendarEventDto(b.Id, "external", "Busy (other calendar)", b.StartAt, b.EndAt, me, null, "Open", "/calendar/connections", null)));
        }

        return events;
    }
}

/// <summary>Every five minutes: customers get the reminders their booking page asks for (plan 7.9), each once.</summary>
public sealed class BookingReminderJob(AppDbContext db, ITenantContext tenant, IEmailSender email, IClock clock, IOptions<AppOptions> app, ILogger<BookingReminderJob> logger)
{
    private const int BatchSize = 500;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var good = MindLift.Infrastructure.Tenancy.TenantAccess.InGoodStanding(db.Tenants, now);
        var upcoming = await db.Bookings.IgnoreQueryFilters().AsNoTracking()
            .Where(b => good.Contains(b.TenantId) && !b.IsDeleted && b.Status == BookingStatus.Confirmed && b.StartAt > now && b.StartAt <= now.AddDays(8))
            .OrderBy(b => b.StartAt).Take(BatchSize).ToListAsync(cancellationToken);

        var sent = 0;
        await TenantBatch.ForEachTenantAsync(upcoming, b => b.TenantId, tenant, async (tenantId, rows) =>
        {
            var pageIds = rows.Select(r => r.BookingPageId).Distinct().ToList();
            var pages = await db.BookingPages.AsNoTracking().Where(p => pageIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
            var workspace = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);

            foreach (var found in rows)
            {
                if (!pages.TryGetValue(found.BookingPageId, out var page))
                {
                    continue;
                }

                var already = BookingJson.Offsets(found.RemindersSent);
                var due = BookingJson.Offsets(page.ReminderOffsetsMinutes).Where(o => !already.Contains(o) && found.StartAt.AddMinutes(-o) <= now).ToList();
                if (due.Count == 0)
                {
                    continue;
                }

                var booking = await db.Bookings.FirstAsync(b => b.Id == found.Id, cancellationToken);
                try
                {
                    var zone = TimeZoneRules.Exists(booking.TimeZone) ? TimeZoneInfo.FindSystemTimeZoneById(booking.TimeZone) : TimeZoneInfo.Utc;
                    var local = TimeZoneInfo.ConvertTimeFromUtc(booking.StartAt, zone).ToString("dddd d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    await email.SendAsync(booking.Email, $"Reminder: {page.Name} on {local}",
                        $"Hello {booking.Name},\n\nA reminder of your booking with {workspace.Name}.\n\n{page.Name}\n{local} ({zone.Id})\n\nTo cancel or choose another time, use the link in your confirmation email.", cancellationToken);
                    sent++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not send the booking reminder for booking {BookingId}", booking.Id);
                    continue;
                }

                // One email covers every reminder that has come due, so a late job run never sends two.
                booking.RemindersSent = BookingJson.Write(already.Concat(due).Distinct().ToList());
            }

            await db.SaveChangesAsync(cancellationToken);
        });

        return sent;
    }
}
