using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MindLift.Application.Common.Ocr;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Documents.Extraction;

/// <summary>
/// Reads a workspace's own document types (plan 19.6): scores a document against each type's keywords, and takes the configured
/// fields out of the text. Anchors and patterns are matched on the digit-normalised text, so Arabic-Indic digits work too.
/// </summary>
public static class CustomDocumentExtractor
{
    public const double PatternConfidence = 90;
    public const double AnchorConfidence = 80;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The keywords and fields stored as JSON on a definition.</summary>
    public static IReadOnlyList<DocumentKeywordDto> Keywords(string json)
        => JsonSerializer.Deserialize<List<DocumentKeywordDto>>(json, Json) ?? [];

    public static IReadOnlyList<DocumentFieldRuleDto> Fields(string json)
        => JsonSerializer.Deserialize<List<DocumentFieldRuleDto>>(json, Json) ?? [];

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>The total weight of the keywords found in the text.</summary>
    public static int Score(IReadOnlyList<DocumentKeywordDto> keywords, string text)
    {
        var haystack = Normalize(text);
        return keywords
            .Where(k => !string.IsNullOrWhiteSpace(k.Text) && haystack.Contains(Normalize(k.Text), StringComparison.OrdinalIgnoreCase))
            .Sum(k => k.Weight);
    }

    /// <summary>Runs every field rule on the text. Fields that are not found come back with Found = false.</summary>
    public static IReadOnlyList<DocumentTypeFieldResultDto> Extract(IReadOnlyList<DocumentFieldRuleDto> rules, string text)
    {
        var haystack = Normalize(text);
        return rules.Select(rule => ExtractOne(rule, haystack)).ToList();
    }

    private static DocumentTypeFieldResultDto ExtractOne(DocumentFieldRuleDto rule, string haystack)
    {
        string? raw = null;
        string? method = null;
        double confidence = 0;

        if (!string.IsNullOrWhiteSpace(rule.Pattern))
        {
            var match = Regex.Match(haystack, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(2));
            if (match.Success)
            {
                raw = (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value).Trim();
                method = "pattern";
                confidence = PatternConfidence;
            }
        }
        else if (!string.IsNullOrWhiteSpace(rule.Anchor))
        {
            var index = haystack.IndexOf(Normalize(rule.Anchor), StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var start = index + rule.Anchor.Length;
                var end = haystack.IndexOfAny(['\n', '\r'], start);
                var rest = haystack[start..(end < 0 ? haystack.Length : end)];
                raw = rest.Trim().TrimStart(':', '-', '–', ' ').Trim();
                method = "anchor";
                confidence = AnchorConfidence;
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new DocumentTypeFieldResultDto(rule.Key, rule.Label, null, 0, rule.Required, false, null);
        }

        var value = Normalize(rule.ValueType, raw);
        if (value is null)
        {
            // The text was found but is not a valid value of this type, for example a date that does not parse.
            return new DocumentTypeFieldResultDto(rule.Key, rule.Label, null, 0, rule.Required, false, method);
        }

        return new DocumentTypeFieldResultDto(rule.Key, rule.Label, value, confidence, rule.Required, true, method);
    }

    /// <summary>The value in the form it is stored in: a date as yyyy-MM-dd, an amount or number in invariant form, text trimmed.</summary>
    public static string? Normalize(string valueType, string raw) => valueType switch
    {
        DocumentValueTypes.Date => DateNormalizer.Parse(raw) is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
        DocumentValueTypes.Amount => AmountNormalizer.Parse(raw) is { Amount: { } amount } ? amount.ToString("F2", CultureInfo.InvariantCulture) : null,
        DocumentValueTypes.Number => decimal.TryParse(Normalize(raw), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number.ToString(CultureInfo.InvariantCulture) : null,
        DocumentValueTypes.Id => Regex.Replace(raw, @"\s+", " ").Trim(),
        _ => raw.Trim(),
    };

    /// <summary>Digits and punctuation in the form Tesseract and people both write them (Arabic-Indic digits become 0–9).</summary>
    public static string Normalize(string? text) => TextNormalizer.NormalizeDigitsAndPunctuation(text);
}
