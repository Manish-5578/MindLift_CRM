using System.Globalization;

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

    /// <summary>Relative text for recent times, otherwise the date (plan 18.7 DateText).</summary>
    public static string Relative(DateTime utc, DateTime nowUtc)
    {
        var span = nowUtc - utc;
        if (span < TimeSpan.Zero)
        {
            var ahead = -span;
            if (ahead.TotalDays < 1) return $"in {Math.Max(1, (int)ahead.TotalHours)} h";
            if (ahead.TotalDays <= 6) return $"in {(int)ahead.TotalDays} days";
            return utc.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }

        if (span.TotalMinutes < 1) return "Just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        if (span.TotalDays < 2) return "Yesterday";
        if (span.TotalDays <= 6) return $"{(int)span.TotalDays} days ago";
        return utc.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    }
}
