using System;
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
    public async Task ExtractAsync_ParsesCompleteBusinessCard_Correctly()
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
    }

    [Fact]
    public async Task ExtractAsync_ThrowsValidationException_WhenTextAndImageAreEmpty()
    {
        await Should.ThrowAsync<ValidationException>(() =>
            _ocrService.ExtractAsync(new ParseCardOcrRequest("   ", null)));
    }

    [Fact]
    public async Task ExtractAsync_ThrowsValidationException_WhenImageProvidedWithoutOcrEngineAndNoText()
    {
        // When OCR engine cannot extract any text, it should throw ValidationException rather than returning dummy data
        await Should.ThrowAsync<ValidationException>(() =>
            _ocrService.ExtractAsync(new ParseCardOcrRequest(null, "invalid_or_empty_image")));
    }
}

