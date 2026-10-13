using System.Globalization;
using System.Text.RegularExpressions;

namespace MindLift.Shared.Validation;

/// <summary>
/// Rules for the tenant brand colour (plan 18.4: a tenant colour only replaces the light-mode primary).
/// Primary buttons carry white text, so a colour that is too light would make them unreadable.
/// </summary>
public static partial class BrandColor
{
    /// <summary>WCAG AA for large and bold text, which is what button labels are.</summary>
    public const double MinimumContrastWithWhite = 3.0;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexPattern();

    public static bool IsHex(string? value) => value is not null && HexPattern().IsMatch(value);

    /// <summary>Contrast ratio between the colour and white, from 1 (same) to 21 (black on white).</summary>
    public static double ContrastWithWhite(string hex)
    {
        var luminance = RelativeLuminance(hex);
        return 1.05 / (luminance + 0.05);
    }

    /// <summary>Returns an error message, or null when the colour is acceptable. Empty means "use the default".</summary>
    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!IsHex(value))
        {
            return "Use a colour like #4F46E5 (a # and six letters or digits).";
        }

        return ContrastWithWhite(value) < MinimumContrastWithWhite
            ? "This colour is too light for white button text. Choose a darker one."
            : null;
    }

    private static double RelativeLuminance(string hex)
    {
        static double Channel(string pair)
        {
            var c = int.Parse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(hex.Substring(1, 2)) + 0.7152 * Channel(hex.Substring(3, 2)) + 0.0722 * Channel(hex.Substring(5, 2));
    }
}
