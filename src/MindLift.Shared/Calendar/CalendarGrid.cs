namespace MindLift.Shared.Calendar;

/// <summary>The date arithmetic behind the calendar screen: which days a view shows, how Previous and Next move, and where a dragged item lands.</summary>
public static class CalendarGrid
{
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";

    /// <summary>The weeks around a month always show six rows of seven days, so the grid does not change height from month to month.</summary>
    private const int MonthGridDays = 42;

    /// <summary>The days a view shows: one day, the seven days of the week that starts on Monday, or six weeks that start on the Monday on or before the 1st. An unknown view is treated as the week.</summary>
    public static IReadOnlyList<DateOnly> Days(string view, DateOnly anchor)
    {
        if (view == Day)
        {
            return [anchor];
        }

        if (view == Month)
        {
            var first = new DateOnly(anchor.Year, anchor.Month, 1);
            var start = first.AddDays(-DaysSinceMonday(first));
            return Enumerable.Range(0, MonthGridDays).Select(start.AddDays).ToList();
        }

        var monday = anchor.AddDays(-DaysSinceMonday(anchor));
        return Enumerable.Range(0, 7).Select(monday.AddDays).ToList();
    }

    /// <summary>The date after pressing Previous (step -1) or Next (step 1): a day, a week or a month. Month steps clamp to the end of a shorter month.</summary>
    public static DateOnly Shift(string view, DateOnly anchor, int step) => view switch
    {
        Day => anchor.AddDays(step),
        Month => anchor.AddMonths(step),
        _ => anchor.AddDays(7 * step),
    };

    /// <summary>The UTC range to load for the shown days: from the start of the first day up to, and not including, the start of the day after the last.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) Range(IReadOnlyList<DateOnly> days)
        => (days[0].ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), days[^1].AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    /// <summary>Where an item lands when it is dropped on a day: that day, at the time of day it already had.</summary>
    public static DateTime Moved(DateTime startUtc, DateOnly day)
        => day.ToDateTime(TimeOnly.FromDateTime(startUtc), DateTimeKind.Utc);

    // ---- In a time zone: the calendar shows each person's own clock, not UTC ----

    /// <summary>The UTC range to load for days in <paramref name="zone"/>: from local midnight of the first day up to local midnight after the last.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) Range(IReadOnlyList<DateOnly> days, TimeZoneInfo zone)
        => (ToUtc(days[0], TimeOnly.MinValue, zone), ToUtc(days[^1].AddDays(1), TimeOnly.MinValue, zone));

    /// <summary>The wall-clock time in <paramref name="zone"/> of a UTC instant.</summary>
    public static DateTime Local(DateTime utc, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>The day in <paramref name="zone"/> a UTC instant falls on.</summary>
    public static DateOnly LocalDay(DateTime utc, TimeZoneInfo zone) => DateOnly.FromDateTime(Local(utc, zone));

    /// <summary>Where an item lands when it is dropped on a day: that day, at the same local time of day it had.</summary>
    public static DateTime Moved(DateTime startUtc, DateOnly day, TimeZoneInfo zone)
        => ToUtc(day, TimeOnly.FromDateTime(Local(startUtc, zone)), zone);

    /// <summary>Where an item lands when it is dropped on an hour of a day: that hour, keeping its minutes past the hour.</summary>
    public static DateTime MovedToHour(DateTime startUtc, DateOnly day, int hour, TimeZoneInfo zone)
        => ToUtc(day, new TimeOnly(Math.Clamp(hour, 0, 23), Local(startUtc, zone).Minute), zone);

    /// <summary>
    /// The UTC instant of a local day and time. A time skipped when clocks go forward is moved on by the gap (02:30 on such a night becomes 03:30);
    /// a time that happens twice when clocks go back is taken as the first.
    /// </summary>
    public static DateTime ToUtc(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        if (zone.IsAmbiguousTime(local))
        {
            // The earlier of the two moments carries the larger (daylight) offset.
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>True for a day in the month grid that belongs to the previous or the next month, which the screen shows dimmed.</summary>
    public static bool IsOtherMonth(DateOnly day, DateOnly anchor) => day.Month != anchor.Month || day.Year != anchor.Year;

    private static int DaysSinceMonday(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;
}
