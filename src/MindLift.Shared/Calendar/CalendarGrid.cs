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

    /// <summary>True for a day in the month grid that belongs to the previous or the next month, which the screen shows dimmed.</summary>
    public static bool IsOtherMonth(DateOnly day, DateOnly anchor) => day.Month != anchor.Month || day.Year != anchor.Year;

    private static int DaysSinceMonday(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;
}
