using System;
using MindLift.Application.Common.Ocr;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Ocr;

public sealed class OcrNormalizerTests
{
    [Fact]
    public void TextNormalizer_Normalizes_Arabic_Indic_Digits()
    {
        var input = "رقم الرخصة: ١٢٣٤٥٦٧٨٩٠";
        var result = TextNormalizer.NormalizeDigitsAndPunctuation(input);
        result.ShouldBe("رقم الرخصة: 1234567890");
    }

    [Fact]
    public void TextNormalizer_Normalizes_Persian_Digits()
    {
        var input = "کد: ۱۲۳۴۵";
        var result = TextNormalizer.NormalizeDigitsAndPunctuation(input);
        result.ShouldBe("کد: 12345");
    }

    [Fact]
    public void TextNormalizer_Normalizes_Punctuation_And_Tatweel()
    {
        var input = "المبلغ: ١٬٢٥٠٫٥٠ ، التــــاريخ";
        var result = TextNormalizer.NormalizeDigitsAndPunctuation(input);
        result.ShouldBe("المبلغ: 1,250.50 , التاريخ");
    }

    [Fact]
    public void TextNormalizer_Normalizes_For_Matching()
    {
        var input = "إعتماد الشّركة بالإدارة";
        var result = TextNormalizer.NormalizeForMatching(input);
        result.ShouldContain("اعتماد");
        result.ShouldContain("الشركه");
        result.ShouldContain("بالاداره");
    }

    [Fact]
    public void DateNormalizer_Parses_Standard_And_Arabic_Indic_Dates()
    {
        DateNormalizer.Parse("15/01/2024").ShouldBe(new DateOnly(2024, 1, 15));
        DateNormalizer.Parse("2026-09-15").ShouldBe(new DateOnly(2026, 9, 15));
        DateNormalizer.Parse("١٤/٠٢/٢٠٢٧").ShouldBe(new DateOnly(2027, 2, 14));
        DateNormalizer.Parse("14-Jan-2027").ShouldBe(new DateOnly(2027, 1, 14));
        DateNormalizer.Parse("١٥ مارس ٢٠٢٥").ShouldBe(new DateOnly(2025, 3, 15));
        DateNormalizer.Parse("Invalid date").ShouldBeNull();
    }

    [Fact]
    public void AmountNormalizer_Parses_Amounts_Currencies_And_Negatives()
    {
        var (amt1, curr1) = AmountNormalizer.Parse("Total Amount Due: $4,935.00");
        amt1.ShouldBe(4935.00m);
        curr1.ShouldBe("USD");

        var (amt2, curr2) = AmountNormalizer.Parse("AED 12,450.50");
        amt2.ShouldBe(12450.50m);
        curr2.ShouldBe("AED");

        var (amt3, _) = AmountNormalizer.Parse("(1,250.00)");
        amt3.ShouldBe(-1250.00m);

        var (amt4, curr4) = AmountNormalizer.Parse("د.إ ١٠٠٠");
        amt4.ShouldBe(1000m);
        curr4.ShouldBe("AED");
    }

    [Fact]
    public void MrzParser_Parses_Valid_TD3_Passport_MRZ()
    {
        var mrzText = @"P<GBRSMITH<<JOHN<ARTHUR<<<<<<<<<<<<<<<<<<<<<
9876543213GBR8501019M2704151<<<<<<<<<<<<<<00";

        var result = MrzParser.Parse(mrzText);
        result.ShouldNotBeNull();
        result.DocumentNumber.ShouldBe("987654321");
        result.IssuingCountry.ShouldBe("GBR");
        result.Nationality.ShouldBe("GBR");
        result.Surname.ShouldBe("SMITH");
        result.GivenNames.ShouldBe("JOHN ARTHUR");
        result.DateOfBirth.ShouldBe(new DateOnly(1985, 1, 1));
        result.Sex.ShouldBe("M");
        result.ExpiryDate.ShouldBe(new DateOnly(2027, 4, 15));
        result.AllCheckDigitsValid.ShouldBeTrue();
        result.Confidence.ShouldBe(1.0);
    }

    [Fact]
    public void MrzParser_Handles_Invalid_Check_Digits_With_Lowered_Confidence()
    {
        // Altered check digit from 3 to 7
        var mrzText = @"P<GBRSMITH<<JOHN<ARTHUR<<<<<<<<<<<<<<<<<<<<<
9876543217GBR8501019M2704151<<<<<<<<<<<<<<00";

        var result = MrzParser.Parse(mrzText);
        result.ShouldNotBeNull();
        result.AllCheckDigitsValid.ShouldBeFalse();
        result.Confidence.ShouldBe(0.60);
    }
}
