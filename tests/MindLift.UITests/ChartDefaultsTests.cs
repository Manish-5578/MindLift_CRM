using ApexCharts;
using MindLift.UI.Components;
using Shouldly;

namespace MindLift.UITests;

/// <summary>Plan 18.7 ChartCard: every chart starts from the same options.</summary>
public sealed class ChartDefaultsTests
{
    private sealed record Row(string Name, decimal Value);

    [Fact]
    public void Series_colours_are_the_plan_palette_in_order()
    {
        var options = ChartDefaults.Create<Row>();

        options.Colors.ShouldBe(["#4F46E5", "#0D9488", "#F59E0B", "#EC4899", "#0EA5E9", "#84CC16", "#8B5CF6", "#F97316"]);
    }

    [Fact]
    public void The_toolbar_and_data_labels_are_off_and_the_legend_sits_at_the_bottom()
    {
        var options = ChartDefaults.Create<Row>();

        options.Chart.Toolbar.Show.ShouldBe(false);
        options.DataLabels.Enabled.ShouldBe(false);
        options.Legend.Position.ShouldBe(LegendPosition.Bottom);
        options.Legend.FontSize.ShouldBe("12px");
    }

    [Fact]
    public void Bars_have_rounded_corners_and_a_45_percent_column_width()
    {
        var options = ChartDefaults.Create<Row>();

        options.PlotOptions.Bar.BorderRadius.ShouldBe(4);
        options.PlotOptions.Bar.ColumnWidth.ShouldBe("45%");
    }

    [Theory]
    [InlineData(false, "#E2E8F0")]
    [InlineData(true, "#273244")]
    public void Grid_lines_and_theme_follow_dark_mode(bool dark, string gridColour)
    {
        var options = ChartDefaults.Create<Row>(dark: dark);

        options.Grid.BorderColor.ShouldBe(gridColour);
        options.Grid.StrokeDashArray.ShouldBe(4);
        options.Tooltip.Theme.ShouldBe(dark ? Mode.Dark : Mode.Light);
        options.Theme.Mode.ShouldBe(dark ? Mode.Dark : Mode.Light);
    }

    [Fact]
    public void In_arabic_the_value_axis_moves_to_the_opposite_side()
    {
        ChartDefaults.Create<Row>(rtl: false).Yaxis.ShouldBeNull();

        var rtl = ChartDefaults.Create<Row>(rtl: true);

        rtl.Yaxis.ShouldNotBeNull().Count.ShouldBe(1);
        rtl.Yaxis![0].Opposite.ShouldBe(true);
    }
}
