using System;
using System.Text;
using System.Text.RegularExpressions;

namespace MindLift.Application.Common.Ocr;

public sealed record MrzResult(
    string DocumentNumber,
    string IssuingCountry,
    string Nationality,
    string Surname,
    string GivenNames,
    DateOnly? DateOfBirth,
    string Sex,
    DateOnly? ExpiryDate,
    bool AllCheckDigitsValid,
    double Confidence);

/// <summary>
/// Parser for ICAO Doc 9303 TD3 (Passport) Machine Readable Zone (MRZ) - 2 lines of 44 characters.
/// </summary>
public static class MrzParser
{
    private static readonly int[] Weights = [7, 3, 1];

    /// <summary>
    /// Attempts to parse TD3 MRZ lines from raw text.
    /// </summary>
    public static MrzResult? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length - 1; i++)
        {
            var l1 = CleanMrzLine(lines[i]);
            var l2 = CleanMrzLine(lines[i + 1]);

            if (l1.Length == 44 && l2.Length == 44 && l1.StartsWith('P'))
            {
                var parsed = TryParseLines(l1, l2);
                if (parsed != null) return parsed;
            }
        }

        return null;
    }

    private static string CleanMrzLine(string line)
    {
        var clean = line.Trim().Replace(" ", "").Replace("«", "<").Replace("(", "<").Replace("[", "<").ToUpperInvariant();
        return clean;
    }

    private static MrzResult? TryParseLines(string l1, string l2)
    {
        // Fix common OCR confusions in numeric positions of L2
        var l2Chars = l2.ToCharArray();

        // Numeric positions in TD3 L2:
        // 9: doc num check digit
        // 13..18: DOB, 19: DOB check
        // 21..26: Expiry, 27: Expiry check
        // 42: optional check, 43: composite check
        int[] numericPositions = [9, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24, 25, 26, 27, 42, 43];
        foreach (var pos in numericPositions)
        {
            if (pos < l2Chars.Length)
            {
                l2Chars[pos] = FixNumericConfusion(l2Chars[pos]);
            }
        }

        var fixedL2 = new string(l2Chars);

        // Document number and check digit
        var docNumField = fixedL2[..9];
        var docNumCheck = fixedL2[9];
        var docValid = ValidateCheckDigit(docNumField, docNumCheck);

        // Nationality
        var nationality = fixedL2.Substring(10, 3).Replace("<", "");

        // DOB
        var dobStr = fixedL2.Substring(13, 6);
        var dobCheck = fixedL2[19];
        var dobValid = ValidateCheckDigit(dobStr, dobCheck);
        var dob = ParseMrzDate(dobStr, isBirthDate: true);

        // Sex
        var sex = fixedL2[20].ToString();

        // Expiry
        var expiryStr = fixedL2.Substring(21, 6);
        var expiryCheck = fixedL2[27];
        var expiryValid = ValidateCheckDigit(expiryStr, expiryCheck);
        var expiry = ParseMrzDate(expiryStr, isBirthDate: false);

        // Issuing country from L1
        var issuingCountry = l1.Length >= 5 ? l1.Substring(2, 3).Replace("<", "") : "";

        // Names from L1 (after position 5)
        var nameSection = l1.Length > 5 ? l1[5..] : "";
        var nameParts = nameSection.Split("<<", StringSplitOptions.None);
        var surname = nameParts.Length > 0 ? nameParts[0].Replace("<", " ").Trim() : "";
        var givenNames = nameParts.Length > 1 ? nameParts[1].Replace("<", " ").Trim() : "";

        bool allValid = docValid && dobValid && expiryValid;
        double confidence = allValid ? 1.0 : 0.60;

        var docNumber = docNumField.Replace("<", "").Trim();

        return new MrzResult(
            DocumentNumber: docNumber,
            IssuingCountry: issuingCountry,
            Nationality: nationality,
            Surname: surname,
            GivenNames: givenNames,
            DateOfBirth: dob,
            Sex: sex,
            ExpiryDate: expiry,
            AllCheckDigitsValid: allValid,
            Confidence: confidence);
    }

    private static char FixNumericConfusion(char c) => c switch
    {
        'O' or 'Q' or 'D' => '0',
        'I' or 'L' => '1',
        'Z' => '2',
        'S' => '5',
        'B' => '8',
        'G' => '6',
        _ => c
    };

    public static bool ValidateCheckDigit(string value, char checkChar)
    {
        if (checkChar is < '0' or > '9') return false;
        int expected = checkChar - '0';
        int sum = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            int charVal;
            if (c is >= '0' and <= '9') charVal = c - '0';
            else if (c is >= 'A' and <= 'Z') charVal = c - 'A' + 10;
            else charVal = 0; // '<' and other non-alphanumeric

            sum += charVal * Weights[i % 3];
        }

        return (sum % 10) == expected;
    }

    private static DateOnly? ParseMrzDate(string yymmdd, bool isBirthDate)
    {
        if (yymmdd.Length != 6) return null;
        if (!int.TryParse(yymmdd[..2], out var yy) ||
            !int.TryParse(yymmdd.Substring(2, 2), out var mm) ||
            !int.TryParse(yymmdd.Substring(4, 2), out var dd))
        {
            return null;
        }

        if (mm is < 1 or > 12 || dd is < 1 or > 31) return null;

        int currentYear = DateTime.UtcNow.Year;
        int currentYY = currentYear % 100;

        int year;
        if (isBirthDate)
        {
            year = yy > currentYY ? 1900 + yy : 2000 + yy;
        }
        else
        {
            // Passports don't expire in the 1900s
            year = 2000 + yy;
        }

        try
        {
            return new DateOnly(year, mm, dd);
        }
        catch
        {
            return null;
        }
    }
}
