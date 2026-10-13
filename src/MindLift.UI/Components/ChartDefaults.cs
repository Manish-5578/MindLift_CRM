using ApexCharts;

namespace MindLift.UI.Components;

/// <summary>
/// The one set of chart options every chart starts from (plan 18.7 ChartCard). A widget only adds its series and anything
/// specific to its chart type, so all charts look alike in light, dark and right-to-left layouts.
/// </summary>
public static class ChartDefaults
{
    /// <summary>Series colours, in this order and no others (plan 18.2).</summary>
    public static readonly string[] Palette = ["#4F46E5", "#0D9488", "#F59E0B", "#EC4899", "#0EA5E9", "#84CC16", "#8B5CF6", "#F97316"];

    /// <summary>Colour for targets and benchmark lines, drawn dashed.</summary>
    public const string TargetColor = "#94A3B8";

    public const string FontFamily = "Inter, IBM Plex Sans Arabic, system-ui, -apple-system, Segoe UI, Roboto, sans-serif";

    public static ApexChartOptions<T> Create<T>(bool dark = false, bool rtl = false) where T : class
    {
        var options = new ApexChartOptions<T>
        {
            Chart = new Chart
            {
                Toolbar = new Toolbar { Show = false },
                FontFamily = FontFamily,
            },
            Colors = [.. Palette],
            Grid = new Grid
            {
                BorderColor = dark ? "#273244" : "#E2E8F0",
                StrokeDashArray = 4,
            },
            DataLabels = new DataLabels { Enabled = false },
            Legend = new Legend { Position = LegendPosition.Bottom, FontSize = "12px" },
            // Width depends on the chart type (2 for lines and areas, 0 for bars), so each widget sets it in Configure.
            Stroke = new Stroke { Curve = Curve.Smooth },
            PlotOptions = new PlotOptions
            {
                Bar = new PlotOptionsBar { BorderRadius = 4, ColumnWidth = "45%" },
            },
            Tooltip = new Tooltip { Theme = dark ? Mode.Dark : Mode.Light },
            Theme = new ApexCharts.Theme { Mode = dark ? Mode.Dark : Mode.Light },
        };

        if (rtl)
        {
            // In Arabic the value axis moves to the right so the chart reads from the right edge.
            options.Yaxis = [new YAxis { Opposite = true }];
        }

        return options;
    }
}
