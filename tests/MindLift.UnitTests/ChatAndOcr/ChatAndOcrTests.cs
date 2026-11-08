using System;
using System.Text;
using System.Threading.Tasks;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Leads;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.ChatAndOcr;

public sealed class ChatAndOcrTests
{
    private readonly LeadCardOcrService _ocrService = new();

    [Fact]
    public async Task ExtractAsync_ParsesCompleteEnglishBusinessCard_Correctly()
    {
        var rawText = @"Apex Global Technologies LLC
Johnathan Smith
Managing Director
+971 50 123 4567
jsmith@apexglobal.test
https://www.apexglobal.test
Level 14, Al Saada Tower, Dubai, UAE";

        var result = await _ocrService.ExtractAsync(new ParseCardOcrRequest(rawText, null));

        result.CompanyName.ShouldBe("Apex Global Technologies LLC");
        result.FullName.ShouldBe("Johnathan Smith");
        result.FirstName.ShouldBe("Johnathan");
        result.LastName.ShouldBe("Smith");
        result.JobTitle.ShouldBe("Managing Director");
        result.Phone.ShouldContain("971 50 123 4567");
        result.Email.ShouldBe("jsmith@apexglobal.test");
        result.Website.ShouldContain("apexglobal.test");
        result.Address.ShouldContain("Dubai");
        result.DetectedScript.ShouldBe("Latin");
        result.OverallConfidence.ShouldBeGreaterThan(0.8);
    }

    [Fact]
    public async Task ExtractAsync_ParsesArabicBusinessCard_Correctly()
    {
        var rawText = @"شركة النور للتكنولوجيا ذ.م.م
محمد عبدالله
المدير العام
+971 4 333 4444
info@alnoortech.ae
شارع الشيخ زايد، برج النور، دبي";

        var result = await _ocrService.ExtractAsync(new ParseCardOcrRequest(rawText, null));

        result.CompanyName.ShouldContain("شركة النور للتكنولوجيا ذ.م.م");
        result.FullName.ShouldBe("محمد عبدالله");
        result.FirstName.ShouldBe("محمد");
        result.LastName.ShouldBe("عبدالله");
        result.JobTitle.ShouldBe("المدير العام");
        result.Phone.ShouldContain("971 4 333 4444");
        result.Email.ShouldBe("info@alnoortech.ae");
        result.Address.ShouldContain("دبي");
        result.DetectedScript.ShouldBe("Bilingual"); // contains Arabic + Latin email
        result.OverallConfidence.ShouldBeGreaterThan(0.8);
    }

    [Fact]
    public async Task ExtractAsync_NormalizesArabicIndicDigits_InPhoneNumber()
    {
        var rawText = @"Apex Technologies LLC
Sami Nasser
Manager
+٩٧١ ٥٠ ١٢٣ ٤٥٦٧
sami@apex.ae";

        var result = await _ocrService.ExtractAsync(new ParseCardOcrRequest(rawText, null));

        result.Phone.ShouldContain("971 50 123 4567");
    }

    [Fact]
    public async Task ExtractAsync_DetectsBilingualScript_Correctly()
    {
        var rawText = @"Vertex Group - مجموعة فيرتكس
Karim Mansour
Managing Director - مدير عام
karim@vertex.test";

        var result = await _ocrService.ExtractAsync(new ParseCardOcrRequest(rawText, null));

        result.DetectedScript.ShouldBe("Bilingual");
        result.CompanyName.ShouldContain("Vertex Group");
    }

    [Fact]
    public async Task ExtractAsync_ThrowsValidationException_WhenTextAndImageAreEmpty()
    {
        await Should.ThrowAsync<ValidationException>(() =>
            _ocrService.ExtractAsync(new ParseCardOcrRequest("   ", null)));
    }

    [Fact]
    public async Task ExtractAsync_ThrowsValidationException_WhenImageExceedsSizeLimit()
    {
        var hugeBase64 = new string('A', 23_000_000);
        await Should.ThrowAsync<ValidationException>(() =>
            _ocrService.ExtractAsync(new ParseCardOcrRequest(null, hugeBase64)));
    }

    [Fact]
    public async Task ExtractAsync_ThrowsValidationException_WhenInvalidImageFormatProvided()
    {
        // 100 bytes of non-image data
        var invalidBytes = Encoding.UTF8.GetBytes(new string('X', 100));
        var base64 = Convert.ToBase64String(invalidBytes);

        await Should.ThrowAsync<ValidationException>(() =>
            _ocrService.ExtractAsync(new ParseCardOcrRequest(null, base64)));
    }
}
