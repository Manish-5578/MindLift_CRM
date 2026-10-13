using MindLift.UI.Components;
using Shouldly;

namespace MindLift.UITests;

public sealed class FormattingTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(12_400, "12.1 KB")]
    [InlineData(1_048_576, "1 MB")]
    [InlineData(3_355_443, "3.2 MB")]
    public void File_sizes_are_shown_in_the_largest_fitting_unit(long bytes, string expected)
    {
        Formatting.FileSize(bytes).ShouldBe(expected);
    }

    [Theory]
    [InlineData(950, "AED 950")]
    [InlineData(9_500, "AED 9.5K")]
    [InlineData(350_000, "AED 350K")]
    [InlineData(1_200_000, "AED 1.2M")]
    [InlineData(3_400_000_000, "AED 3.4B")]
    public void Compact_money_follows_the_plan_rules(long amount, string expected)
    {
        Formatting.Compact(amount, "AED").ShouldBe(expected);
    }

    [Fact]
    public void Full_money_uses_western_digits_and_two_decimals()
    {
        Formatting.Full(1_250_000m, "AED").ShouldBe("AED 1,250,000.00");
    }

    [Fact]
    public void Recent_times_are_shown_relatively_and_old_ones_as_dates()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        Formatting.Relative(now.AddSeconds(-20), now).ShouldBe("Just now");
        Formatting.Relative(now.AddMinutes(-5), now).ShouldBe("5 min ago");
        Formatting.Relative(now.AddHours(-3), now).ShouldBe("3 h ago");
        Formatting.Relative(now.AddDays(-1), now).ShouldBe("Yesterday");
        Formatting.Relative(now.AddDays(-10), now).ShouldBe("29 Sep 2026");
        Formatting.Relative(now.AddDays(2.5), now).ShouldBe("in 2 days");
    }
}
