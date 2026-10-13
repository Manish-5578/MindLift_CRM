using MudBlazor;

namespace MindLift.UI.Theme;

/// <summary>
/// The MudBlazor theme. Values from plan section 18.2–18.4. Tenant brand colours only change the light primary.
/// </summary>
public static class MindLiftTheme
{
    private static readonly string[] FontStack =
        ["Inter", "IBM Plex Sans Arabic", "system-ui", "-apple-system", "Segoe UI", "Roboto", "sans-serif"];

    public static MudTheme Create(string? tenantPrimary = null) => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = tenantPrimary ?? "#4F46E5",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#0D9488",
            SecondaryContrastText = "#FFFFFF",
            Tertiary = "#F59E0B",
            Info = "#2563EB",
            Success = "#16A34A",
            Warning = "#D97706",
            Error = "#DC2626",
            Dark = "#0F172A",
            Background = "#F8FAFC",
            BackgroundGray = "#F1F5F9",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#0F172A",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#334155",
            DrawerIcon = "#64748B",
            TextPrimary = "#0F172A",
            TextSecondary = "#475569",
            TextDisabled = "#94A3B8",
            ActionDefault = "#64748B",
            ActionDisabled = "#CBD5E1",
            ActionDisabledBackground = "#F1F5F9",
            Divider = "#E2E8F0",
            DividerLight = "#F1F5F9",
            LinesDefault = "#E2E8F0",
            LinesInputs = "#CBD5E1",
            TableLines = "#E2E8F0",
            TableStriped = "#F8FAFC",
            TableHover = "#F1F5F9",
            GrayLight = "#E2E8F0",
            GrayLighter = "#F1F5F9",
            HoverOpacity = 0.06,
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#818CF8",
            PrimaryContrastText = "#0B1120",
            Secondary = "#2DD4BF",
            Tertiary = "#FBBF24",
            Info = "#60A5FA",
            Success = "#4ADE80",
            Warning = "#FBBF24",
            Error = "#F87171",
            Dark = "#E5E7EB",
            Background = "#0B1120",
            BackgroundGray = "#1F2937",
            Surface = "#111827",
            AppbarBackground = "#111827",
            AppbarText = "#E5E7EB",
            DrawerBackground = "#111827",
            DrawerText = "#D1D5DB",
            DrawerIcon = "#9CA3AF",
            TextPrimary = "#E5E7EB",
            TextSecondary = "#9CA3AF",
            TextDisabled = "#6B7280",
            ActionDefault = "#9CA3AF",
            ActionDisabled = "#4B5563",
            ActionDisabledBackground = "#1F2937",
            Divider = "#273244",
            DividerLight = "#1F2937",
            LinesDefault = "#273244",
            LinesInputs = "#374151",
            TableLines = "#273244",
            TableStriped = "#0F172A",
            TableHover = "#1F2937",
            HoverOpacity = 0.08,
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = FontStack, FontSize = "0.875rem", FontWeight = "400", LineHeight = "1.43", LetterSpacing = "normal" },
            H4 = new H4Typography { FontFamily = FontStack, FontSize = "1.5rem", FontWeight = "600", LineHeight = "1.333", LetterSpacing = "-0.01em" },
            H5 = new H5Typography { FontFamily = FontStack, FontSize = "1.25rem", FontWeight = "600", LineHeight = "1.4", LetterSpacing = "-0.005em" },
            H6 = new H6Typography { FontFamily = FontStack, FontSize = "1rem", FontWeight = "600", LineHeight = "1.5", LetterSpacing = "normal" },
            Subtitle1 = new Subtitle1Typography { FontFamily = FontStack, FontSize = "0.9375rem", FontWeight = "500", LineHeight = "1.467", LetterSpacing = "normal" },
            Subtitle2 = new Subtitle2Typography { FontFamily = FontStack, FontSize = "0.875rem", FontWeight = "600", LineHeight = "1.43", LetterSpacing = "normal" },
            Body1 = new Body1Typography { FontFamily = FontStack, FontSize = "0.875rem", FontWeight = "400", LineHeight = "1.43", LetterSpacing = "normal" },
            Body2 = new Body2Typography { FontFamily = FontStack, FontSize = "0.8125rem", FontWeight = "400", LineHeight = "1.385", LetterSpacing = "normal" },
            Caption = new CaptionTypography { FontFamily = FontStack, FontSize = "0.75rem", FontWeight = "500", LineHeight = "1.333", LetterSpacing = "normal" },
            Overline = new OverlineTypography { FontFamily = FontStack, FontSize = "0.6875rem", FontWeight = "600", LineHeight = "1.455", LetterSpacing = "0.06em" },
            Button = new ButtonTypography { FontFamily = FontStack, FontSize = "0.875rem", FontWeight = "600", LineHeight = "1.43", LetterSpacing = "normal", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            AppbarHeight = "56px",
            DrawerWidthLeft = "248px",
            DrawerWidthRight = "480px",
            DrawerMiniWidthLeft = "72px",
        },
    };
}
