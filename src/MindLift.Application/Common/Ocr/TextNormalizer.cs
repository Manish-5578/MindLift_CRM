using System;
using System.Text;
using System.Text.RegularExpressions;

namespace MindLift.Application.Common.Ocr;

/// <summary>
/// Normalizes text from OCR output: replaces Arabic-Indic and Eastern Arabic digits with standard ASCII 0-9,
/// normalizes punctuation (Arabic comma, decimal/thousands separators), removes tatweel, and collapses whitespace.
/// </summary>
public static class TextNormalizer
{
    private static readonly Regex HorizontalWhitespaceRegex = new(@"[^\S\r\n]+", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes digits and common Arabic typography while preserving original letters and line breaks.
    /// </summary>
    public static string NormalizeDigitsAndPunctuation(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is >= '\u0660' and <= '\u0669') // Arabic-Indic digits ٠-٩
            {
                sb.Append((char)('0' + (ch - '\u0660')));
            }
            else if (ch is >= '\u06F0' and <= '\u06F9') // Eastern Arabic / Persian digits ۰-۹
            {
                sb.Append((char)('0' + (ch - '\u06F0')));
            }
            else if (ch == '\u060C') // Arabic comma ،
            {
                sb.Append(',');
            }
            else if (ch == '\u066B') // Arabic decimal separator ٫
            {
                sb.Append('.');
            }
            else if (ch == '\u066C') // Arabic thousands separator ٬
            {
                sb.Append(',');
            }
            else if (ch == '\u0640') // Tatweel ـ
            {
                // drop tatweel
            }
            else
            {
                sb.Append(ch);
            }
        }

        return HorizontalWhitespaceRegex.Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>
    /// Normalizes text specifically for keyword and rule matching: unifies Alef forms (أ إ آ -> ا), ى -> ي, ة -> ه.
    /// </summary>
    public static string NormalizeForMatching(string? input)
    {
        var text = NormalizeDigitsAndPunctuation(input);
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // Strip Arabic diacritics (harakat / tashkeel)
            if (ch is >= '\u064B' and <= '\u0652' or '\u0670')
            {
                continue;
            }

            switch (ch)
            {
                case '\u0622': // آ
                case '\u0623': // أ
                case '\u0625': // إ
                case '\u0671': // ٱ
                    sb.Append('\u0627'); // ا
                    break;
                case '\u0649': // ى
                    sb.Append('\u064A'); // ي
                    break;
                case '\u0629': // ة
                    sb.Append('\u0647'); // ه
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString().ToLowerInvariant();
    }
}
