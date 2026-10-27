using System.Text;
using System.Text.RegularExpressions;

namespace MindLift.Infrastructure.Documents.Text;

/// <summary>
/// What reading Arabic needs beyond reading Latin text: which way a line runs, the many ways one number can be written, and the invisible
/// direction marks the engine adds. Cleaning the text here means dates, amounts and ids can be found the same way as in an English document.
/// </summary>
public static partial class ArabicText
{
    private const string ArabicIndic = "٠١٢٣٤٥٦٧٨٩";
    private const string ExtendedArabicIndic = "۰۱۲۳۴۵۶۷۸۹";   // the digits used with Persian and Urdu

    /// <summary>True for a letter written right to left (Arabic, Hebrew and their extensions).</summary>
    public static bool IsRightToLeftLetter(char c)
        => char.IsLetter(c) && ((c >= '֐' && c <= 'ࣿ') || (c >= 'יִ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿'));

    /// <summary>The share (0 to 1) of the letters in the text that are right-to-left.</summary>
    public static double RightToLeftShare(string text)
    {
        int rtl = 0, letters = 0;
        foreach (var c in text)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (IsRightToLeftLetter(c)) rtl++;
        }

        return letters == 0 ? 0 : (double)rtl / letters;
    }

    /// <summary>True when most of the letters read right to left, so the line is read from its right edge.</summary>
    public static bool RunsRightToLeft(string text) => RightToLeftShare(text) > 0.5;

    /// <summary>
    /// Removes direction marks, writes Arabic-Indic and Persian digits as ordinary digits, and turns the Arabic comma, semicolon and decimal
    /// separator into the usual ones, so the text can be searched and its dates, amounts and ids read.
    /// </summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = FixMixedDigits(text);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (IsDirectionMark(c)) continue;
            var a = ArabicIndic.IndexOf(c);
            if (a >= 0) { sb.Append((char)('0' + a)); continue; }
            var p = ExtendedArabicIndic.IndexOf(c);
            if (p >= 0) { sb.Append((char)('0' + p)); continue; }
            sb.Append(c switch { '،' => ',', '؛' => ';', '٫' => '.', '٬' => ',', '؟' => '?', _ => c });
        }

        return sb.ToString();
    }

    /// <summary>
    /// The Arabic-Indic five (٥) is drawn like a round zero, and engines often read it as 0. A written number does not mix the two kinds of digit,
    /// so a 0 inside a number that also has Arabic-Indic digits is taken to be a five.
    /// </summary>
    private static string FixMixedDigits(string text)
    {
        if (text.IndexOfAny(ArabicIndic.ToCharArray()) < 0 || !text.Contains('0')) return text;
        return MixedNumber().Replace(text, m => m.Value.Replace('0', '٥'));
    }

    [GeneratedRegex(@"[٠-٩][٠-٩\d]*|\d[٠-٩\d]*[٠-٩][٠-٩\d]*")]
    private static partial Regex MixedNumber();

    private static bool IsDirectionMark(char c)
        => c is '\u200E' or '\u200F' or '\u061C' || (c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069');

    /// <summary>
    /// An engine can hand back the parts of a number written with dashes in the wrong order inside a right-to-left line.
    /// Emirates ID numbers (784-YYYY-NNNNNNN-C) are recognisable by their fixed shape, so a reversed one is put right.
    /// </summary>
    public static string FixLine(string line)
    {
        if (!RunsRightToLeft(line)) return line;
        return ReversedEmiratesId().Replace(line, m => $"784-{m.Groups[3].Value}-{m.Groups[2].Value}-{m.Groups[1].Value}");
    }

    [GeneratedRegex(@"(?<!\d)(\d)-(\d{7})-(\d{4})-784(?!\d)")]
    private static partial Regex ReversedEmiratesId();
}
