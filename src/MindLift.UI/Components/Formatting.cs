using System.Globalization;
using MindLift.UI.Localization;

namespace MindLift.UI.Components;

/// <summary>Number, money and date formats shared by every screen (plan 18.7, 18.13).</summary>
public static class Formatting
{
    /// <summary>Western digits in both languages. Currency code first, then the amount.</summary>
    public static string Full(decimal amount, string currency)
        => $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    /// <summary>1,250 → "1,250"; 9,500 → "9.5K"; 350,000 → "350K"; 1,200,000 → "1.2M"; 3,400,000,000 → "3.4B".</summary>
    public static string Compact(decimal amount, string currency)
    {
        var abs = Math.Abs(amount);
        var sign = amount < 0 ? "-" : string.Empty;
        string body = abs switch
        {
            < 1_000 => abs.ToString("N0", CultureInfo.InvariantCulture),
            < 10_000 => (abs / 1_000m).ToString("0.0", CultureInfo.InvariantCulture) + "K",
            < 1_000_000 => (abs / 1_000m).ToString("0", CultureInfo.InvariantCulture) + "K",
            < 1_000_000_000 => (abs / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "M",
            _ => (abs / 1_000_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "B",
        };
        return $"{currency} {sign}{body}";
    }

    /// <summary>512 → "512 B"; 12,400 → "12.1 KB"; 3,355,443 → "3.2 MB". Western digits in both languages.</summary>
    public static string FileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{(bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture)} KB";
        }

        return $"{(bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture)} MB";
    }

    private static readonly string[] ArabicMonths =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    private static readonly string[] ArabicDays = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];

    /// <summary>"15 Oct 2026", or "15 أكتوبر 2026" in Arabic. Western digits in both languages.</summary>
    public static string ShortDate(DateTime date, Translator? translator = null)
        => translator is { IsArabic: true }
            ? $"{date.Day.ToString("00", CultureInfo.InvariantCulture)} {ArabicMonths[date.Month - 1]} {date.Year.ToString(CultureInfo.InvariantCulture)}"
            : date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes a date with a .NET pattern such as "MMM dd, yyyy HH:mm". In English the result is exactly what <c>ToString(pattern)</c> gives, so nothing changes there.
    /// In Arabic the month and day names are Arabic, and the digits stay Western. A one-letter pattern is a standard format: "g" is date and time, "t" is the time.
    /// </summary>
    public static string Format(DateTime value, string pattern, Translator? translator = null)
    {
        if (translator is not { IsArabic: true })
        {
            return pattern.Length == 1 ? value.ToString(pattern) : value.ToString(pattern, CultureInfo.InvariantCulture);
        }

        if (pattern == "g")
        {
            pattern = "dd MMM yyyy HH:mm";
        }
        else if (pattern == "t")
        {
            pattern = "HH:mm";
        }

        var text = new System.Text.StringBuilder();
        for (var i = 0; i < pattern.Length;)
        {
            var c = pattern[i];
            if (c == '\'')
            {
                var end = pattern.IndexOf('\'', i + 1);
                end = end < 0 ? pattern.Length : end;
                text.Append(pattern, i + 1, end - i - 1);
                i = end + 1;
                continue;
            }

            if (!char.IsLetter(c))
            {
                text.Append(c);
                i++;
                continue;
            }

            var run = 1;
            while (i + run < pattern.Length && pattern[i + run] == c)
            {
                run++;
            }

            text.Append(ArabicToken(value, c, run));
            i += run;
        }

        return text.ToString();
    }

    /// <summary>The same as <see cref="Format(DateTime, string, Translator?)"/> for a date with no time.</summary>
    public static string Format(DateOnly value, string pattern, Translator? translator = null)
        => Format(value.ToDateTime(TimeOnly.MinValue), pattern, translator);

    private static string ArabicToken(DateTime value, char letter, int run)
    {
        string Number(int number, int width) => number.ToString(new string('0', Math.Max(1, width)), CultureInfo.InvariantCulture);

        return letter switch
        {
            'd' when run >= 3 => ArabicDays[(int)value.DayOfWeek],
            'd' => Number(value.Day, run),
            'M' when run >= 3 => ArabicMonths[value.Month - 1],
            'M' => Number(value.Month, run),
            'y' => run == 2 ? Number(value.Year % 100, 2) : value.Year.ToString(CultureInfo.InvariantCulture),
            'H' => Number(value.Hour, run),
            'h' => Number(value.Hour % 12 == 0 ? 12 : value.Hour % 12, run),
            'm' => Number(value.Minute, run),
            's' => Number(value.Second, run),
            _ => new string(letter, run),
        };
    }

    /// <summary>"Thursday, 15 October 2026, 14:30 UTC", or the same in Arabic.</summary>
    public static string FullDateTime(DateTime utc, Translator? translator = null)
        => translator is { IsArabic: true }
            ? $"{ArabicDays[(int)utc.DayOfWeek]}، {ShortDate(utc, translator)}، {utc.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC"
            : utc.ToString("dddd, dd MMMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Relative text for recent times, otherwise the date (plan 18.7 DateText). Pass the translator to read it in Arabic.</summary>
    public static string Relative(DateTime utc, DateTime nowUtc, Translator? translator = null)
    {
        string Say(string english, params object[] values)
            => string.Format(CultureInfo.InvariantCulture, translator is null ? english : translator[english], values);

        var span = nowUtc - utc;
        if (span < TimeSpan.Zero)
        {
            var ahead = -span;
            if (ahead.TotalDays < 1) return Say("in {0} h", Math.Max(1, (int)ahead.TotalHours));
            if (ahead.TotalDays <= 6) return Say("in {0} days", (int)ahead.TotalDays);
            return ShortDate(utc, translator);
        }

        if (span.TotalMinutes < 1) return Say("Just now");
        if (span.TotalMinutes < 60) return Say("{0} min ago", (int)span.TotalMinutes);
        if (span.TotalHours < 24) return Say("{0} h ago", (int)span.TotalHours);
        if (span.TotalDays < 2) return Say("Yesterday");
        if (span.TotalDays <= 6) return Say("{0} days ago", (int)span.TotalDays);
        return ShortDate(utc, translator);
    }
}
