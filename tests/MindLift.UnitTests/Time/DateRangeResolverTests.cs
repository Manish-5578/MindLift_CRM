using MindLift.Application.Common.Time;
using MindLift.Shared.Enums;
using Shouldly;

namespace MindLift.UnitTests.Time;

/// <summary>Plan section 4.7: presets are resolved in the tenant's time zone, never in UTC or server time.</summary>
public sealed class DateRangeResolverTests
{
    private static readonly TimeZoneInfo Dubai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void Acceptance_example_a_deal_won_at_2230_utc_on_31_october_counts_in_november_for_dubai()
    {
        // 31 Oct 2026 22:30 UTC is 1 Nov 2026 02:30 in Dubai (UTC+4).
        var now = new DateTime(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc);

        var range = DateRangeResolver.Resolve(DatePreset.ThisMonth, now, Dubai);

        range.StartUtc.ShouldBe(new DateTime(2026, 10, 31, 20, 0, 0, DateTimeKind.Utc));
        range.EndUtc.ShouldBe(new DateTime(2026, 11, 30, 20, 0, 0, DateTimeKind.Utc));
        range.Contains(new DateTime(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc)).ShouldBeTrue();
    }

    [Fact]
    public void Last_month_for_dubai_on_the_same_instant_is_october()
    {
        var now = new DateTime(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc);

        var range = DateRangeResolver.Resolve(DatePreset.LastMonth, now, Dubai);

        range.StartUtc.ShouldBe(new DateTime(2026, 9, 30, 20, 0, 0, DateTimeKind.Utc));
        range.EndUtc.ShouldBe(new DateTime(2026, 10, 31, 20, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void This_month_for_new_york_respects_daylight_saving_time_at_the_start_of_november()
    {
        // 5 Nov 2026 in New York. DST ended on 1 Nov, so midnight on 1 Nov is UTC-4 (EDT).
        var now = new DateTime(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc);

        var range = DateRangeResolver.Resolve(DatePreset.ThisMonth, now, NewYork);

        range.StartUtc.ShouldBe(new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void This_week_starts_on_monday_by_default_and_on_sunday_when_configured()
    {
        // Friday 9 Oct 2026 (local Dubai noon).
        var now = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);

        var monday = DateRangeResolver.Resolve(DatePreset.ThisWeek, now, Dubai, weekStart: DayOfWeek.Monday);
        var sunday = DateRangeResolver.Resolve(DatePreset.ThisWeek, now, Dubai, weekStart: DayOfWeek.Sunday);

        monday.StartUtc.ShouldBe(new DateTime(2026, 10, 4, 20, 0, 0, DateTimeKind.Utc)); // Mon 5 Oct, Dubai midnight
        sunday.StartUtc.ShouldBe(new DateTime(2026, 10, 3, 20, 0, 0, DateTimeKind.Utc)); // Sun 4 Oct, Dubai midnight
    }

    [Fact]
    public void This_quarter_follows_an_april_fiscal_year()
    {
        // Oct 2026 is in fiscal Q3 (Oct-Dec) when the fiscal year starts in April.
        var now = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);

        var range = DateRangeResolver.Resolve(DatePreset.ThisQuarter, now, Dubai, fiscalYearStartMonth: 4);

        range.StartUtc.ShouldBe(new DateTime(2026, 9, 30, 20, 0, 0, DateTimeKind.Utc)); // 1 Oct 2026 Dubai
        range.EndUtc.ShouldBe(new DateTime(2026, 12, 31, 20, 0, 0, DateTimeKind.Utc));  // 1 Jan 2027 Dubai
    }

    [Fact]
    public void This_year_with_an_april_fiscal_year_starts_in_april_of_the_current_fiscal_year()
    {
        var now = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);

        var range = DateRangeResolver.Resolve(DatePreset.ThisYear, now, Dubai, fiscalYearStartMonth: 4);

        range.StartUtc.ShouldBe(new DateTime(2026, 3, 31, 20, 0, 0, DateTimeKind.Utc)); // 1 Apr 2026 Dubai
        range.EndUtc.ShouldBe(new DateTime(2027, 3, 31, 20, 0, 0, DateTimeKind.Utc));   // 1 Apr 2027 Dubai
    }

    [Fact]
    public void Last_seven_days_includes_today()
    {
        var now = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);

        var range = DateRangeResolver.Resolve(DatePreset.LastNDays, now, Dubai, n: 7);

        range.StartUtc.ShouldBe(new DateTime(2026, 10, 2, 20, 0, 0, DateTimeKind.Utc)); // 3 Oct Dubai
        range.EndUtc.ShouldBe(new DateTime(2026, 10, 9, 20, 0, 0, DateTimeKind.Utc));   // 10 Oct Dubai (exclusive)
    }

    [Fact]
    public void Next_seven_days_starts_today()
    {
        var now = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);

        var range = DateRangeResolver.Resolve(DatePreset.NextNDays, now, Dubai, n: 7);

        range.StartUtc.ShouldBe(new DateTime(2026, 10, 8, 20, 0, 0, DateTimeKind.Utc)); // 9 Oct Dubai
        range.EndUtc.ShouldBe(new DateTime(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc));  // 16 Oct Dubai
    }

    [Fact]
    public void Quarter_before_the_first_quarter_wraps_into_the_previous_fiscal_year()
    {
        // Jan 2027 with an April fiscal year: last quarter is Oct-Dec 2026.
        var now = new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc);

        var range = DateRangeResolver.Resolve(DatePreset.LastQuarter, now, Dubai, fiscalYearStartMonth: 4);

        range.StartUtc.ShouldBe(new DateTime(2026, 9, 30, 20, 0, 0, DateTimeKind.Utc)); // 1 Oct 2026 Dubai
        range.EndUtc.ShouldBe(new DateTime(2026, 12, 31, 20, 0, 0, DateTimeKind.Utc));  // 1 Jan 2027 Dubai
    }
}
