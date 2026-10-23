using MindLift.Shared.Calendar;
using Shouldly;

namespace MindLift.UnitTests.Calendar;

/// <summary>The days each calendar view shows and where a dragged item lands. October 2026 starts on a Thursday and February 2027 starts on a Monday.</summary>
public sealed class CalendarGridTests
{
    [Theory]
    [InlineData(2026, 10, 12)] // Monday
    [InlineData(2026, 10, 14)] // Wednesday
    [InlineData(2026, 10, 18)] // Sunday still belongs to the week that started on Monday the 12th
    public void A_week_runs_from_Monday_to_Sunday_around_any_day_in_it(int year, int month, int day)
    {
        var days = CalendarGrid.Days(CalendarGrid.Week, new DateOnly(year, month, day));

        days.Count.ShouldBe(7);
        days[0].ShouldBe(new DateOnly(2026, 10, 12));
        days[6].ShouldBe(new DateOnly(2026, 10, 18));
    }

    [Fact]
    public void A_day_view_shows_only_that_day()
    {
        CalendarGrid.Days(CalendarGrid.Day, new DateOnly(2026, 10, 14)).ShouldBe([new DateOnly(2026, 10, 14)]);
    }

    [Fact]
    public void A_month_that_starts_mid_week_begins_on_the_Monday_before_the_first()
    {
        var days = CalendarGrid.Days(CalendarGrid.Month, new DateOnly(2026, 10, 20));

        days.Count.ShouldBe(42);
        days[0].ShouldBe(new DateOnly(2026, 9, 28));
        days[41].ShouldBe(new DateOnly(2026, 11, 8));
        days.ShouldContain(new DateOnly(2026, 10, 1));
        days.ShouldContain(new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void A_month_that_starts_on_a_Monday_has_no_days_before_the_first()
    {
        var days = CalendarGrid.Days(CalendarGrid.Month, new DateOnly(2027, 2, 15));

        days[0].ShouldBe(new DateOnly(2027, 2, 1));
        days[41].ShouldBe(new DateOnly(2027, 3, 14));
    }

    [Fact]
    public void Every_month_grid_has_six_weeks_that_start_on_a_Monday_and_contain_the_whole_month()
    {
        for (var offset = 0; offset < 36; offset++)
        {
            var anchor = new DateOnly(2026, 1, 15).AddMonths(offset);
            var days = CalendarGrid.Days(CalendarGrid.Month, anchor);

            days.Count.ShouldBe(42);
            days[0].DayOfWeek.ShouldBe(DayOfWeek.Monday);
            days.ShouldContain(new DateOnly(anchor.Year, anchor.Month, 1));
            days.ShouldContain(new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month)));
            days.Zip(days.Skip(1), (a, b) => b.DayNumber - a.DayNumber).ShouldAllBe(gap => gap == 1);
        }
    }

    [Fact]
    public void An_unknown_view_is_shown_as_a_week()
    {
        CalendarGrid.Days("agenda", new DateOnly(2026, 10, 14)).Count.ShouldBe(7);
    }

    [Fact]
    public void Previous_and_Next_move_by_a_day_a_week_or_a_month()
    {
        var anchor = new DateOnly(2026, 10, 14);

        CalendarGrid.Shift(CalendarGrid.Day, anchor, 1).ShouldBe(new DateOnly(2026, 10, 15));
        CalendarGrid.Shift(CalendarGrid.Day, anchor, -1).ShouldBe(new DateOnly(2026, 10, 13));
        CalendarGrid.Shift(CalendarGrid.Week, anchor, 1).ShouldBe(new DateOnly(2026, 10, 21));
        CalendarGrid.Shift(CalendarGrid.Week, anchor, -1).ShouldBe(new DateOnly(2026, 10, 7));
        CalendarGrid.Shift(CalendarGrid.Month, anchor, 1).ShouldBe(new DateOnly(2026, 11, 14));
        CalendarGrid.Shift(CalendarGrid.Month, anchor, -1).ShouldBe(new DateOnly(2026, 9, 14));
    }

    [Fact]
    public void Moving_by_a_month_clamps_to_a_shorter_month_and_crosses_the_year()
    {
        CalendarGrid.Shift(CalendarGrid.Month, new DateOnly(2027, 1, 31), 1).ShouldBe(new DateOnly(2027, 2, 28));
        CalendarGrid.Shift(CalendarGrid.Month, new DateOnly(2027, 3, 31), -1).ShouldBe(new DateOnly(2027, 2, 28));
        CalendarGrid.Shift(CalendarGrid.Month, new DateOnly(2026, 12, 20), 1).ShouldBe(new DateOnly(2027, 1, 20));
        CalendarGrid.Shift(CalendarGrid.Month, new DateOnly(2027, 1, 10), -1).ShouldBe(new DateOnly(2026, 12, 10));
    }

    [Fact]
    public void The_range_to_load_covers_the_first_day_up_to_the_start_of_the_day_after_the_last()
    {
        var (from, to) = CalendarGrid.Range(CalendarGrid.Days(CalendarGrid.Week, new DateOnly(2026, 10, 14)));

        from.ShouldBe(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));
        to.ShouldBe(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc));
        from.Kind.ShouldBe(DateTimeKind.Utc);
        to.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void A_dropped_item_keeps_its_time_of_day_on_the_new_day()
    {
        var start = new DateTime(2026, 10, 14, 9, 15, 30, DateTimeKind.Utc);

        var moved = CalendarGrid.Moved(start, new DateOnly(2026, 10, 20));

        moved.ShouldBe(new DateTime(2026, 10, 20, 9, 15, 30, DateTimeKind.Utc));
        moved.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void An_item_late_in_the_evening_stays_on_the_day_it_was_dropped_on()
    {
        var moved = CalendarGrid.Moved(new DateTime(2026, 10, 14, 23, 45, 0, DateTimeKind.Utc), new DateOnly(2026, 10, 15));

        moved.Date.ShouldBe(new DateTime(2026, 10, 15));
        moved.TimeOfDay.ShouldBe(new TimeSpan(23, 45, 0));
    }

    [Fact]
    public void Days_from_the_neighbouring_months_are_marked_so_the_screen_can_dim_them()
    {
        var anchor = new DateOnly(2026, 10, 20);

        CalendarGrid.IsOtherMonth(new DateOnly(2026, 9, 28), anchor).ShouldBeTrue();
        CalendarGrid.IsOtherMonth(new DateOnly(2026, 10, 5), anchor).ShouldBeFalse();
        CalendarGrid.IsOtherMonth(new DateOnly(2026, 11, 2), anchor).ShouldBeTrue();
        CalendarGrid.IsOtherMonth(new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 10)).ShouldBeTrue();
    }

    // ---- In a time zone ----

    private static readonly TimeZoneInfo Dubai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");       // UTC+4, no daylight saving
    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");   // UTC+5:30
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");   // daylight saving

    [Fact]
    public void A_week_in_Dubai_loads_from_local_midnight_not_UTC_midnight()
    {
        var (from, to) = CalendarGrid.Range(CalendarGrid.Days(CalendarGrid.Week, new DateOnly(2026, 10, 14)), Dubai);

        from.ShouldBe(new DateTime(2026, 10, 11, 20, 0, 0, DateTimeKind.Utc));
        to.ShouldBe(new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_meeting_late_in_the_UTC_evening_is_on_the_next_day_in_Dubai()
    {
        var utc = new DateTime(2026, 10, 14, 21, 30, 0, DateTimeKind.Utc);

        CalendarGrid.LocalDay(utc, Dubai).ShouldBe(new DateOnly(2026, 10, 15));
        CalendarGrid.Local(utc, Dubai).ShouldBe(new DateTime(2026, 10, 15, 1, 30, 0));
    }

    [Fact]
    public void Moving_to_another_day_keeps_the_local_time_of_day()
    {
        // 09:15 in Kolkata is 03:45 UTC.
        var start = new DateTime(2026, 10, 14, 3, 45, 0, DateTimeKind.Utc);

        var moved = CalendarGrid.Moved(start, new DateOnly(2026, 10, 20), Kolkata);

        CalendarGrid.Local(moved, Kolkata).ShouldBe(new DateTime(2026, 10, 20, 9, 15, 0));
    }

    [Fact]
    public void Dropping_on_an_hour_moves_to_that_hour_and_keeps_the_minutes()
    {
        var start = new DateTime(2026, 10, 14, 5, 15, 0, DateTimeKind.Utc); // 09:15 Dubai

        var moved = CalendarGrid.MovedToHour(start, new DateOnly(2026, 10, 16), 14, Dubai);

        moved.ShouldBe(new DateTime(2026, 10, 16, 10, 15, 0, DateTimeKind.Utc)); // 14:15 Dubai
    }

    [Fact]
    public void Moving_across_a_clock_change_keeps_the_wall_clock_time()
    {
        // 10:00 in London is 09:00 UTC in summer and 10:00 UTC in winter; clocks went back on 25 October 2026.
        var summer = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

        var winter = CalendarGrid.Moved(summer, new DateOnly(2026, 10, 27), London);

        winter.ShouldBe(new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_time_skipped_when_clocks_go_forward_moves_on_by_the_gap()
    {
        // In London, 01:00 to 02:00 does not exist on 28 March 2027.
        CalendarGrid.ToUtc(new DateOnly(2027, 3, 28), new TimeOnly(1, 30), London)
            .ShouldBe(new DateTime(2027, 3, 28, 1, 30, 0, DateTimeKind.Utc)); // 02:30 BST
    }

    [Fact]
    public void A_time_that_happens_twice_when_clocks_go_back_is_the_first_one()
    {
        // In London, 01:30 happens twice on 25 October 2026: first at 00:30 UTC (BST), then at 01:30 UTC.
        CalendarGrid.ToUtc(new DateOnly(2026, 10, 25), new TimeOnly(1, 30), London)
            .ShouldBe(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
    }
}
