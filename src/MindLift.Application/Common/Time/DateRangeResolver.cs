using MindLift.Shared.Enums;

namespace MindLift.Application.Common.Time;

/// <summary>
/// Turns a named preset into a UTC range using the tenant's time zone (plan section 4.7).
/// Dashboards and reports must use this; never DateTime.Today.
/// </summary>
public static class DateRangeResolver
{
    /// <param name="preset">The named range.</param>
    /// <param name="nowUtc">The current instant in UTC.</param>
    /// <param name="zone">The tenant (or user) time zone.</param>
    /// <param name="n">Number of days for LastNDays / NextNDays.</param>
    /// <param name="weekStart">First day of the week (tenant setting).</param>
    /// <param name="fiscalYearStartMonth">1 = calendar year; 4 = April start, and so on.</param>
    public static DateRange Resolve(
        DatePreset preset,
        DateTime nowUtc,
        TimeZoneInfo zone,
        int n = 0,
        DayOfWeek weekStart = DayOfWeek.Monday,
        int fiscalYearStartMonth = 1)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone).Date;
        var thisWeekStart = StartOfWeek(today, weekStart);

        var (from, to) = preset switch
        {
            DatePreset.Today => (today, today.AddDays(1)),
            DatePreset.Yesterday => (today.AddDays(-1), today),
            DatePreset.ThisWeek => (thisWeekStart, thisWeekStart.AddDays(7)),
            DatePreset.LastWeek => (thisWeekStart.AddDays(-7), thisWeekStart),
            DatePreset.ThisMonth => MonthRange(today, 0),
            DatePreset.LastMonth => MonthRange(today, -1),
            DatePreset.ThisQuarter => QuarterRange(today, fiscalYearStartMonth, 0),
            DatePreset.LastQuarter => QuarterRange(today, fiscalYearStartMonth, -1),
            DatePreset.ThisYear => YearRange(today, fiscalYearStartMonth, 0),
            DatePreset.LastYear => YearRange(today, fiscalYearStartMonth, -1),
            DatePreset.LastNDays => (today.AddDays(-(n - 1)), today.AddDays(1)),
            DatePreset.NextNDays => (today, today.AddDays(n)),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown date preset."),
        };

        return new DateRange(ToUtc(from, zone), ToUtc(to, zone));
    }

    /// <summary>Monday-based or Sunday-based week start for the given local date.</summary>
    public static DateTime StartOfWeek(DateTime localDate, DayOfWeek weekStart)
    {
        var diff = ((int)localDate.DayOfWeek - (int)weekStart + 7) % 7;
        return localDate.Date.AddDays(-diff);
    }

    private static (DateTime From, DateTime To) MonthRange(DateTime today, int monthOffset)
    {
        var first = new DateTime(today.Year, today.Month, 1).AddMonths(monthOffset);
        return (first, first.AddMonths(1));
    }

    private static (DateTime From, DateTime To) QuarterRange(DateTime today, int fiscalStartMonth, int quarterOffset)
    {
        var fiscalYearStart = FiscalYearStart(today, fiscalStartMonth);
        var monthsIntoFiscalYear = (today.Month - fiscalStartMonth + 12) % 12;
        var quarterIndex = monthsIntoFiscalYear / 3;
        var from = fiscalYearStart.AddMonths(3 * (quarterIndex + quarterOffset));
        return (from, from.AddMonths(3));
    }

    private static (DateTime From, DateTime To) YearRange(DateTime today, int fiscalStartMonth, int yearOffset)
    {
        var from = FiscalYearStart(today, fiscalStartMonth).AddYears(yearOffset);
        return (from, from.AddYears(1));
    }

    private static DateTime FiscalYearStart(DateTime today, int fiscalStartMonth)
    {
        var year = today.Month >= fiscalStartMonth ? today.Year : today.Year - 1;
        return new DateTime(year, fiscalStartMonth, 1);
    }

    private static DateTime ToUtc(DateTime localDate, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), zone);
}
