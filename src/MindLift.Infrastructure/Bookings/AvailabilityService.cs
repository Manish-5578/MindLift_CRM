using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Bookings;

/// <summary>
/// Works out which times a booking page offers (plan 7.9): the page's opening hours in its own time zone, minus meetings
/// already in the diary (with the buffers around them), minus anything too soon or too far ahead. Round robin pages offer a time
/// when any one of their people is free.
/// </summary>
public sealed class AvailabilityService(AppDbContext db, IClock clock)
{
    private static readonly TimeSpan DefaultMeeting = TimeSpan.FromHours(1);

    /// <summary>Start times (UTC) from <paramref name="fromUtc"/> up to but not including <paramref name="toUtc"/>.</summary>
    /// <param name="ignoreActivityId">A meeting to leave out of the diary, so a booking being moved does not block its own new time.</param>
    public async Task<IReadOnlyList<SlotDto>> SlotsAsync(BookingPage page, DateTime fromUtc, DateTime toUtc, Guid? ignoreActivityId, CancellationToken cancellationToken)
    {
        var people = await CandidatesAsync(page, cancellationToken);
        if (people.Count == 0 || toUtc <= fromUtc)
        {
            return [];
        }

        var now = clock.UtcNow;
        var earliest = now.AddHours(page.MinNoticeHours);
        var latest = now.AddDays(page.MaxDaysAhead);
        var windowStart = fromUtc > earliest ? fromUtc : earliest;
        var windowEnd = toUtc < latest ? toUtc : latest;
        if (windowEnd <= windowStart)
        {
            return [];
        }

        var duration = TimeSpan.FromMinutes(page.DurationMinutes);
        var before = TimeSpan.FromMinutes(page.BufferBeforeMinutes);
        var after = TimeSpan.FromMinutes(page.BufferAfterMinutes);
        var busy = await BusyAsync(people, windowStart - before - TimeSpan.FromHours(12), windowEnd + duration + after + TimeSpan.FromHours(12), ignoreActivityId, cancellationToken);

        var availability = BookingJson.Availability(page.Availability);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(availability.TimeZone);
        var step = TimeSpan.FromMinutes(page.SlotStepMinutes);

        // Walk the page's local days that can touch the window, with a day of margin each side for time zone shifts.
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(windowStart, zone)).AddDays(-1);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(windowEnd, zone)).AddDays(1);
        var slots = new SortedSet<DateTime>();

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            foreach (var range in RangesFor(availability, day))
            {
                var from = BookingJson.Minutes(range.From);
                var to = BookingJson.Minutes(range.To);
                if (from is null || to is null)
                {
                    continue;
                }

                var rangeEnd = day.ToDateTime(TimeOnly.MinValue).AddMinutes(to.Value);
                for (var local = day.ToDateTime(TimeOnly.MinValue).AddMinutes(from.Value); local + duration <= rangeEnd; local += step)
                {
                    if (zone.IsInvalidTime(local))
                    {
                        continue;
                    }

                    var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
                    if (start < windowStart || start >= windowEnd || slots.Contains(start))
                    {
                        continue;
                    }

                    if (people.Any(person => IsFree(busy, person, start - before, start + duration + after)))
                    {
                        slots.Add(start);
                    }
                }
            }
        }

        return slots.Select(s => new SlotDto(s, s + duration)).ToList();
    }

    /// <summary>The people who are free for a meeting at this start time, in the order the page lists them.</summary>
    public async Task<IReadOnlyList<Guid>> FreePeopleAsync(BookingPage page, DateTime startUtc, Guid? ignoreActivityId, CancellationToken cancellationToken)
    {
        var people = await CandidatesAsync(page, cancellationToken);
        var before = TimeSpan.FromMinutes(page.BufferBeforeMinutes);
        var after = TimeSpan.FromMinutes(page.BufferAfterMinutes);
        var end = startUtc.AddMinutes(page.DurationMinutes);
        var busy = await BusyAsync(people, startUtc - before - TimeSpan.FromHours(12), end + after + TimeSpan.FromHours(12), ignoreActivityId, cancellationToken);
        return people.Where(p => IsFree(busy, p, startUtc - before, end + after)).ToList();
    }

    /// <summary>The people who take bookings for the page: the first active one for a single person page, all of them for round robin.</summary>
    private async Task<List<Guid>> CandidatesAsync(BookingPage page, CancellationToken cancellationToken)
    {
        var listed = BookingJson.Users(page.UserIds);
        var active = await db.Users.AsNoTracking().Where(u => listed.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken);
        var ordered = listed.Where(active.Contains).ToList();
        return page.AssignmentMode == "RoundRobin" ? ordered : ordered.Take(1).ToList();
    }

    private async Task<Dictionary<Guid, List<(DateTime Start, DateTime End)>>> BusyAsync(IReadOnlyList<Guid> people, DateTime from, DateTime to, Guid? ignoreActivityId, CancellationToken cancellationToken)
    {
        var meetings = await db.Activities.AsNoTracking()
            .Where(a => a.Type == ActivityTypes.Meeting && a.Status == "Open" && a.DueAt != null && people.Contains(a.OwnerId) && a.Id != ignoreActivityId
                && a.DueAt < to && (a.EndAt == null ? a.DueAt.Value.AddHours(1) > from : a.EndAt > from))
            .Select(a => new { a.OwnerId, Start = a.DueAt!.Value, a.EndAt })
            .ToListAsync(cancellationToken);
        var busy = meetings.GroupBy(m => m.OwnerId).ToDictionary(g => g.Key, g => g.Select(m => (m.Start, m.EndAt ?? m.Start + DefaultMeeting)).ToList());

        // What is already taken in the person's connected Google or Microsoft calendar blocks the same times.
        var external = await db.ExternalBusyBlocks.AsNoTracking()
            .Where(b => people.Contains(b.UserId) && b.StartAt < to && b.EndAt > from).Select(b => new { b.UserId, b.StartAt, b.EndAt }).ToListAsync(cancellationToken);
        foreach (var g in external.GroupBy(b => b.UserId))
        {
            if (!busy.TryGetValue(g.Key, out var list)) busy[g.Key] = list = [];
            list.AddRange(g.Select(b => (b.StartAt, b.EndAt)));
        }

        return busy;
    }

    private static bool IsFree(Dictionary<Guid, List<(DateTime Start, DateTime End)>> busy, Guid person, DateTime from, DateTime to)
        => !busy.TryGetValue(person, out var intervals) || intervals.All(i => i.End <= from || i.Start >= to);

    private static IReadOnlyList<TimeRangeDto> RangesFor(AvailabilityDto availability, DateOnly day)
    {
        var key = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var exception = availability.DateOverrides.FirstOrDefault(o => o.Date == key);
        if (exception is not null)
        {
            return exception.Ranges;
        }

        var dayKey = BookingJson.DayKeys[((int)day.DayOfWeek + 6) % 7];
        return availability.Weekly.TryGetValue(dayKey, out var ranges) ? ranges : [];
    }
}
