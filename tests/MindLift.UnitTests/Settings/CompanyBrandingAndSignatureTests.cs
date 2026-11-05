using System;
using MindLift.Api.Validation;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Settings;

public sealed class CompanyBrandingAndSignatureTests
{
    [Fact]
    public void CompanySettingsDto_Preserves_Branding_And_Signature_Fields()
    {
        var dto = new CompanySettingsDto(
            CompanyName: "Acme Global Corp",
            Email: "contact@acme.com",
            Phone: "+971 4 123 4567",
            BaseCurrency: "AED",
            DefaultLanguage: "en",
            TimeZoneId: "Asia/Dubai",
            WeekStartDay: 1,
            FiscalYearStartMonth: 1,
            TaxEnabled: true,
            TaxLabel: "VAT",
            DefaultTaxRate: 5.0m,
            TaxRegistrationNumber: "TRN-99887766",
            PrimaryColor: "#10B981",
            Address: "Level 24, Emirates Tower, Dubai, UAE",
            LogoUrl: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAUA...",
            SignatureUrl: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAB...",
            SignatoryName: "Jane Doe",
            SignatoryTitle: "Chief Executive Officer");

        dto.CompanyName.ShouldBe("Acme Global Corp");
        dto.Address.ShouldBe("Level 24, Emirates Tower, Dubai, UAE");
        dto.LogoUrl.ShouldStartWith("data:image/png;base64,");
        dto.SignatureUrl.ShouldStartWith("data:image/png;base64,");
        dto.SignatoryName.ShouldBe("Jane Doe");
        dto.SignatoryTitle.ShouldBe("Chief Executive Officer");
    }

    [Fact]
    public void SaveCompanySettingsRequest_Defaults_Optional_Branding_To_Null()
    {
        var req = new SaveCompanySettingsRequest(
            CompanyName: "TechVentures LLC",
            Email: "billing@techventures.io",
            Phone: null,
            DefaultLanguage: "en",
            TimeZoneId: "UTC",
            WeekStartDay: 1,
            FiscalYearStartMonth: 1,
            TaxEnabled: false,
            TaxLabel: null,
            DefaultTaxRate: 0m,
            TaxRegistrationNumber: null,
            PrimaryColor: null);

        req.CompanyName.ShouldBe("TechVentures LLC");
        req.Address.ShouldBeNull();
        req.LogoUrl.ShouldBeNull();
        req.SignatureUrl.ShouldBeNull();
        req.SignatoryName.ShouldBeNull();
        req.SignatoryTitle.ShouldBeNull();
    }

    [Fact]
    public void SaveCompanySettingsRequestValidator_Validates_Signatory_And_Address()
    {
        var validator = new SaveCompanySettingsRequestValidator();

        var valid = new SaveCompanySettingsRequest(
            CompanyName: "Apex Corp",
            Email: "info@apex.com",
            Phone: "12345678",
            DefaultLanguage: "en",
            TimeZoneId: "UTC",
            WeekStartDay: 1,
            FiscalYearStartMonth: 1,
            TaxEnabled: false,
            TaxLabel: null,
            DefaultTaxRate: 0m,
            TaxRegistrationNumber: null,
            PrimaryColor: "#4F46E5",
            Address: "100 Silicon Way",
            LogoUrl: "https://apex.com/logo.png",
            SignatureUrl: "Jane Doe",
            SignatoryName: "Jane Doe",
            SignatoryTitle: "Managing Director");

        var result = validator.Validate(valid);
        result.IsValid.ShouldBeTrue();

        var invalid = valid with
        {
            CompanyName = "",
            Address = new string('A', 501),
            SignatoryName = new string('B', 101),
            SignatoryTitle = new string('C', 101)
        };

        var invalidResult = validator.Validate(invalid);
        invalidResult.IsValid.ShouldBeFalse();
        invalidResult.Errors.ShouldContain(e => e.PropertyName == "CompanyName");
        invalidResult.Errors.ShouldContain(e => e.PropertyName == "Address");
        invalidResult.Errors.ShouldContain(e => e.PropertyName == "SignatoryName");
        invalidResult.Errors.ShouldContain(e => e.PropertyName == "SignatoryTitle");
    }

    [Fact]
    public void PublicCheckoutDto_Includes_Company_Branding()
    {
        var checkout = new PublicCheckoutDto
        {
            Id = Guid.NewGuid(),
            LinkNumber = "PL-2001",
            Title = "Invoice INV-1002 Settlement",
            Currency = "USD",
            Amount = 1200m,
            PaidAmount = 0m,
            RemainingAmount = 1200m,
            Status = "Active",
            CompanyName = "Vertex Technologies",
            CompanyLogoUrl = "https://vertex.io/assets/brand-logo.png"
        };

        checkout.CompanyName.ShouldBe("Vertex Technologies");
        checkout.CompanyLogoUrl.ShouldBe("https://vertex.io/assets/brand-logo.png");
    }

    [Fact]
    public void QuoteESignDetailsDto_Includes_Company_Endorsement()
    {
        var details = new QuoteESignDetailsDto
        {
            QuoteId = Guid.NewGuid(),
            QuoteNumber = "QUO-9912",
            Title = "Managed Cloud Services",
            Currency = "USD",
            GrandTotal = 5000m,
            CompanyName = "Vertex Technologies",
            CompanyLogoUrl = "https://vertex.io/assets/brand-logo.png",
            CompanySignatureUrl = "https://vertex.io/assets/ceo-sign.png",
            CompanySignatoryName = "Alexander Wright",
            CompanySignatoryTitle = "Managing Director"
        };

        details.CompanyName.ShouldBe("Vertex Technologies");
        details.CompanyLogoUrl.ShouldBe("https://vertex.io/assets/brand-logo.png");
        details.CompanySignatureUrl.ShouldBe("https://vertex.io/assets/ceo-sign.png");
        details.CompanySignatoryName.ShouldBe("Alexander Wright");
        details.CompanySignatoryTitle.ShouldBe("Managing Director");
    }
}

