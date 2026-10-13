using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MindLift.Application.Common.Ocr;

/// <summary>
/// Normalizes and parses dates found in OCR text across common English and Arabic formats.
/// </summary>
public static class DateNormalizer
{
    private static readonly string[] DayFirstFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyy.MM.dd",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "dd.MM.yyyy",
        "d.M.yyyy",
        "dd MMM yyyy",
        "d MMM yyyy",
        "dd MMMM yyyy",
        "d MMMM yyyy",
        "dd-MMM-yyyy",
        "d-MMM-yyyy",
        "dd/MM/yy",
        "d/M/yy",
        "dd-MM-yy",
        "d-M-yy",
        "MMM dd, yyyy",
        "MMMM d, yyyy",
        "MM/dd/yyyy",
        "M/d/yyyy"
    ];

    private static readonly Regex DateCandidateRegex = new(
        @"(?<!\d)(\d{1,4}[/\-.\s](?:[A-Za-z]{3,9}|\d{1,2})[/\-.\s]\d{1,4})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses a date string into a DateOnly.
    /// </summary>
    public static DateOnly? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var normalized = TextNormalizer.NormalizeDigitsAndPunctuation(input);

        // Try direct parsing first
        if (TryParseExact(normalized, out var result))
        {
            return result;
        }

        // Try extracting a date token from candidate string
        var match = DateCandidateRegex.Match(normalized);
        if (match.Success && TryParseExact(match.Groups[1].Value.Trim(), out var candidateResult))
        {
            return candidateResult;
        }

        return null;
    }

    private static bool TryParseExact(string candidate, out DateOnly result)
    {
        result = default;
        var clean = candidate.Trim().TrimEnd('.', ',', ';');

        // Check Arabic month name replacements if any
        clean = NormalizeArabicMonths(clean);

        if (DateTime.TryParseExact(clean, DayFirstFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return ValidateAndSet(dt, out result);
        }

        if (DateTime.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dt))
        {
            return ValidateAndSet(dt, out result);
        }

        return false;
    }

    private static bool ValidateAndSet(DateTime dt, out DateOnly result)
    {
        result = default;
        var year = dt.Year;
        if (year < 100)
        {
            year = year < 50 ? 2000 + year : 1900 + year;
            dt = new DateTime(year, dt.Month, dt.Day);
        }

        if (year is < 1900 or > 2100)
        {
            return false;
        }

        result = DateOnly.FromDateTime(dt);
        return true;
    }

    private static string NormalizeArabicMonths(string s)
    {
        // Simple mapping of common Arabic month transliterations/translations
        return s.Replace("يناير", "Jan")
                .Replace("فبراير", "Feb")
                .Replace("مارس", "Mar")
                .Replace("أبريل", "Apr")
                .Replace("ابريل", "Apr")
                .Replace("مايو", "May")
                .Replace("يونيو", "Jun")
                .Replace("يوليو", "Jul")
                .Replace("أغسطس", "Aug")
                .Replace("اغسطس", "Aug")
                .Replace("سبتمبر", "Sep")
                .Replace("أكتوبر", "Oct")
                .Replace("اكتوبر", "Oct")
                .Replace("نوفمبر", "Nov")
                .Replace("ديسمبر", "Dec");
    }
}
