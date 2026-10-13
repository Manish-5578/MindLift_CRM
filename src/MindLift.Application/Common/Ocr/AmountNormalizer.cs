using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MindLift.Application.Common.Ocr;

/// <summary>
/// Normalizes and parses monetary amounts and currencies from OCR text.
/// </summary>
public static class AmountNormalizer
{
    private static readonly Regex AmountRegex = new(
        @"(?<currency>AED|USD|SAR|EUR|GBP|INR|QAR|OMR|BHD|KWD|\$|€|£|د\.إ|ر\.س)?\s*" +
        @"(?<![A-Za-z0-9])(?<amount>\(?-?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,2})?\)?)\s*" +
        @"(?<suffixCurrency>AED|USD|SAR|EUR|GBP|INR|QAR|OMR|BHD|KWD|د\.إ|ر\.س)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses monetary amount and optional currency code from input string.
    /// </summary>
    public static (decimal? Amount, string? Currency) Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (null, null);

        var normalized = TextNormalizer.NormalizeDigitsAndPunctuation(input);
        var match = AmountRegex.Match(normalized);
        if (!match.Success) return (null, null);

        var amountStr = match.Groups["amount"].Value.Trim();
        var currency = match.Groups["currency"].Success && !string.IsNullOrWhiteSpace(match.Groups["currency"].Value)
            ? NormalizeCurrency(match.Groups["currency"].Value.Trim())
            : (match.Groups["suffixCurrency"].Success && !string.IsNullOrWhiteSpace(match.Groups["suffixCurrency"].Value)
                ? NormalizeCurrency(match.Groups["suffixCurrency"].Value.Trim())
                : null);

        bool isNegative = false;
        if (amountStr.StartsWith('(') && amountStr.EndsWith(')'))
        {
            isNegative = true;
            amountStr = amountStr[1..^1];
        }
        else if (amountStr.StartsWith('-'))
        {
            isNegative = true;
            amountStr = amountStr[1..];
        }

        amountStr = amountStr.Replace(",", "").Trim();

        if (decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return (isNegative ? -value : value, currency);
        }

        return (null, currency);
    }

    private static string NormalizeCurrency(string curr)
    {
        return curr.ToUpperInvariant() switch
        {
            "$" => "USD",
            "€" => "EUR",
            "£" => "GBP",
            "د.إ" => "AED",
            "ر.س" => "SAR",
            _ => curr.ToUpperInvariant()
        };
    }
}
