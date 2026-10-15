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
