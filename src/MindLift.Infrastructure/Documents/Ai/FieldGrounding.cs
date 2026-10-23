using System.Text.RegularExpressions;
using MindLift.Application.Common.Ocr;
using MindLift.Infrastructure.Documents.Text;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Documents.Ai;

/// <summary>How firmly a value the model gave is found in the document's own text. Higher is firmer.</summary>
public enum GroundLevel
{
    /// <summary>The value is nowhere in the text. The model may have made it up.</summary>
    None = 0,

    /// <summary>Every word of the value is in the text, but not together in that order.</summary>
    Words = 1,

    /// <summary>The same value written another way: other spacing or hyphens, another date layout, the same number with other separators.</summary>
    Equivalent = 2,

    /// <summary>The value is in the text exactly as the model gave it.</summary>
    Exact = 3,
}

public sealed record GroundResult(GroundLevel Level, string? Snippet, int? Page)
{
    public bool IsFound => Level != GroundLevel.None;

    /// <summary>How sure to be of a value found at this level, 0 to 1.</summary>
    public double Confidence => Level switch
    {
        GroundLevel.Exact => 0.95,
        GroundLevel.Equivalent => 0.85,
        GroundLevel.Words => 0.7,
        _ => 0.4,
    };
}

/// <summary>
/// The guard against made-up values. A language model can answer with something that sounds right and is not in the document;
/// every value is looked up in the text the document was read from, and one that cannot be found there is marked unverified for a person to check.
/// </summary>
public static class FieldGrounding
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex Squashable = new(@"[\s\-–—_]+", RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex Words = new(@"[\p{L}\p{N}]{2,}", RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex DateLike = new(
        @"\d{1,4}\s?[/.\-]\s?\d{1,2}\s?[/.\-]\s?\d{1,4}|\d{1,2}\s+[A-Za-z]{3,9}\.?,?\s+\d{2,4}|[A-Za-z]{3,9}\.?\s+\d{1,2},?\s+\d{4}",
        RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex NumberLike = new(@"\d[\d,.\s]*\d|\d", RegexOptions.Compiled, MatchTimeout);

    public static GroundResult Verify(string? value, string valueType, IReadOnlyList<PageText> pages)
    {
        var wanted = Flat(value);
        if (wanted.Length == 0)
        {
            return new GroundResult(GroundLevel.None, null, null);
        }

        var flat = pages.Select(p => (p.PageNumber, Text: Flat(p.Text))).ToList();

        foreach (var (number, text) in flat)
        {
            var at = text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                return new GroundResult(GroundLevel.Exact, Snip(text, at, wanted.Length), number);
            }
        }

        var squashedWanted = Squash(wanted);
        if (squashedWanted.Length >= 4)
        {
            foreach (var (number, text) in flat)
            {
                if (Squash(text).Contains(squashedWanted, StringComparison.OrdinalIgnoreCase))
                {
                    return new GroundResult(GroundLevel.Equivalent, SnipAround(text, wanted), number);
                }
            }
        }

        var byType = valueType switch
        {
            DocumentValueTypes.Date => FindDate(wanted, flat),
            DocumentValueTypes.Amount or DocumentValueTypes.Number => FindNumber(wanted, flat),
            DocumentValueTypes.Phone => FindPhone(wanted, flat),
            _ => null,
        };
        if (byType is not null)
        {
            return byType;
        }

        var tokens = Words.Matches(wanted).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tokens.Count > 0)
        {
            foreach (var (number, text) in flat)
            {
                if (tokens.All(t => Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(t)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase, MatchTimeout)))
                {
                    return new GroundResult(GroundLevel.Words, SnipAround(text, wanted), number);
                }
            }
        }

        return new GroundResult(GroundLevel.None, null, null);
    }

    private static GroundResult? FindDate(string wanted, List<(int PageNumber, string Text)> pages)
    {
        if (DateNormalizer.Parse(wanted) is not { } target)
        {
            return null;
        }

        foreach (var (number, text) in pages)
        {
            foreach (Match match in DateLike.Matches(text))
            {
                if (DateNormalizer.Parse(match.Value) == target)
                {
                    return new GroundResult(GroundLevel.Equivalent, Snip(text, match.Index, match.Length), number);
                }
            }
        }

        return null;
    }

    private static GroundResult? FindNumber(string wanted, List<(int PageNumber, string Text)> pages)
    {
        var target = AmountNormalizer.Parse(wanted).Amount;
        if (target is null)
        {
            return null;
        }

        foreach (var (number, text) in pages)
        {
            foreach (Match match in NumberLike.Matches(text))
            {
                if (AmountNormalizer.Parse(match.Value.Trim()).Amount == target)
                {
                    return new GroundResult(GroundLevel.Equivalent, Snip(text, match.Index, match.Length), number);
                }
            }
        }

        return null;
    }

    private static GroundResult? FindPhone(string wanted, List<(int PageNumber, string Text)> pages)
    {
        var digits = new string(wanted.Where(char.IsDigit).ToArray());
        if (digits.Length < 7)
        {
            return null;
        }

        foreach (var (number, text) in pages)
        {
            var all = new string(text.Where(char.IsDigit).ToArray());
            if (all.Contains(digits, StringComparison.Ordinal))
            {
                return new GroundResult(GroundLevel.Equivalent, SnipAround(text, digits[..Math.Min(6, digits.Length)]), number);
            }
        }

        return null;
    }

    /// <summary>The text with Arabic digits turned into 0 to 9 and every run of white space made into one space, so line breaks do not hide a value.</summary>
    private static string Flat(string? text) => Whitespace.Replace(TextNormalizer.NormalizeDigitsAndPunctuation(text), " ").Trim();

    private static string Squash(string text) => Squashable.Replace(text, string.Empty);

    private static string Snip(string text, int index, int length)
    {
        var from = Math.Max(0, index - 40);
        var to = Math.Min(text.Length, index + length + 40);
        return (from > 0 ? "…" : string.Empty) + text[from..to].Trim() + (to < text.Length ? "…" : string.Empty);
    }

    /// <summary>A snippet near the first long word of the value, for a value found only in another form.</summary>
    private static string? SnipAround(string text, string wanted)
    {
        var anchor = Words.Matches(wanted).Select(m => m.Value).OrderByDescending(w => w.Length).FirstOrDefault();
        if (anchor is null)
        {
            return null;
        }

        var at = text.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? null : Snip(text, at, anchor.Length);
    }

    public static string Describe(GroundLevel level) => level switch
    {
        GroundLevel.Exact => "found in the document",
        GroundLevel.Equivalent => "found in the document in another form",
        GroundLevel.Words => "its words are in the document",
        _ => "not found in the document text",
    };
}
