using System.Threading.Tasks;
using MindLift.Infrastructure.Leads;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.ChatAndOcr;

/// <summary>An address is never the contact person, a six-digit PIN is never a phone number, and front and back readings merge without losing or inventing values.</summary>
public sealed class CardClassificationTests
{
    private readonly LeadCardOcrService _service = new();

    private Task<ExtractedLeadCardDto> ParseAsync(string text) => _service.ExtractAsync(new ParseCardOcrRequest(text, null));

    private const string IndianCard =
        "Rahul Sharma\nSenior Sales Manager\nAcme Industries Pvt Ltd\n+91 98765 43210\nrahul@acme.in\nwww.acme.in\n" +
        "No. 42, 3rd Cross, HSR Layout\nLandmark: Near ASC Hospital\nBengaluru - 560 102";

    [Fact]
    public async Task Landmark_Near_ASC_MustBecomeAddress_NeverContactPerson()
    {
        var card = await ParseAsync("Landmark: Near ASC\nAcme Foods Pvt Ltd\ninfo@acmefoods.in");

        card.FullName.ShouldNotBe("Landmark: Near ASC");
        card.FirstName.ShouldNotBe("Landmark:");
        card.Address.ShouldNotBeNull();
        card.Address!.ShouldContain("Landmark: Near ASC");
    }

    [Fact]
    public async Task IndianPinCode_ExtractedAsAddress()
    {
        var card = await ParseAsync(IndianCard);

        card.FullName.ShouldBe("Rahul Sharma");
        card.JobTitle.ShouldBe("Senior Sales Manager");
        card.CompanyName.ShouldBe("Acme Industries Pvt Ltd");
        card.Email.ShouldBe("rahul@acme.in");
        card.Phone.ShouldNotBeNull();
        card.Phone!.ShouldContain("98765");
        card.Address.ShouldNotBeNull();
        card.Address!.ShouldContain("560 102");
        card.Address.ShouldContain("Landmark: Near ASC Hospital");
        card.Address.ShouldContain("HSR Layout");
        card.AddressConfidence.ShouldNotBeNull();
        card.AddressConfidence!.Value.ShouldBeGreaterThanOrEqualTo(0.8);
    }

    [Fact]
    public async Task A_pin_code_with_a_space_is_not_taken_for_a_phone_number()
    {
        var card = await ParseAsync("Meera Nair\nSunrise Traders LLC\nBengaluru 560 001\n+91 80 4123 9988");

        card.Phone.ShouldNotBeNull();
        card.Phone!.ShouldContain("4123");
        card.Address.ShouldNotBeNull();
        card.Address!.ShouldContain("560 001");
    }

    [Fact]
    public async Task A_company_name_that_contains_a_city_stays_a_company()
    {
        var card = await ParseAsync("Dubai Gold Trading LLC\nOmar Haddad\nomar@dubaigold.ae");

        card.CompanyName.ShouldBe("Dubai Gold Trading LLC");
        card.FullName.ShouldBe("Omar Haddad");
        card.Address.ShouldBeNull();
    }

    [Fact]
    public async Task A_name_that_matches_the_email_is_trusted_more_than_a_left_over_line()
    {
        var matching = await ParseAsync("Zaid Al-Mansoor\nHorizon Dynamics LLC\nzaid@horizondynamics.test");
        var leftOver = await ParseAsync("Kiran Patel\nHorizon Dynamics LLC\ninfo@horizondynamics.test");

        matching.NameConfidence.ShouldBe(0.85);
        leftOver.NameConfidence.ShouldBe(0.60);
    }

    [Fact]
    public async Task A_name_made_up_from_the_email_alone_is_marked_unreliable()
    {
        var card = await ParseAsync("Horizon Dynamics LLC\njane.doe@horizondynamics.test");

        card.FullName.ShouldBe("Jane Doe");
        card.NameConfidence.ShouldBe(0.50);
    }

    [Fact]
    public async Task BackSide_Email_MergedFromFrontAndBack()
    {
        var front = await ParseAsync("Rahul Sharma\nSenior Sales Manager\nAcme Industries Pvt Ltd\n+91 98765 43210");
        var back = await ParseAsync("rahul@acme.in\nwww.acme.in\nNo. 42, 3rd Cross, HSR Layout\nBengaluru - 560 102");

        var merged = await _service.MergeFrontAndBackAsync(front, back);

        merged.FullName.ShouldBe("Rahul Sharma");
        merged.Email.ShouldBe("rahul@acme.in");
        merged.Website.ShouldBe("www.acme.in");
        merged.Phone!.ShouldContain("98765");
        merged.Address!.ShouldContain("560 102");
        merged.RawText.ShouldContain("[Front]");
        merged.RawText.ShouldContain("[Back]");
        merged.Conflicts.ShouldBeNull();
    }

    [Fact]
    public async Task The_same_email_phone_and_address_on_both_sides_is_kept_once()
    {
        var side = "Rahul Sharma\nAcme Industries Pvt Ltd\nrahul@acme.in\n+91 98765 43210\nHSR Layout, Bengaluru 560 102";
        var front = await ParseAsync(side);
        var back = await ParseAsync("RAHUL@ACME.IN\n+91 98765 43210\nHSR Layout, Bengaluru 560 102");

        var merged = await _service.MergeFrontAndBackAsync(front, back);

        merged.Email.ShouldBe("rahul@acme.in");
        merged.Conflicts.ShouldBeNull();
        merged.Address!.Split(',').Length.ShouldBe(front.Address!.Split(',').Length);
    }

    [Fact]
    public async Task ConflictingNamesAcrossSides_ReturnsBothCandidates()
    {
        var front = await ParseAsync("Rahul Sharma\nAcme Industries Pvt Ltd\nrahul@acme.in");
        var back = await ParseAsync("Rahul Kumar Verma\nAcme Industries Pvt Ltd");

        var merged = await _service.MergeFrontAndBackAsync(front, back);

        merged.Conflicts.ShouldNotBeNull();
        var conflict = merged.Conflicts!.ShouldHaveSingleItem();
        conflict.Field.ShouldBe("Name");
        conflict.FrontValue.ShouldBe("Rahul Sharma");
        conflict.BackValue.ShouldBe("Rahul Kumar Verma");
        merged.FullName.ShouldBe("Rahul Sharma");
    }

    [Fact]
    public async Task Evidence_boxes_are_tagged_with_the_side_they_came_from()
    {
        var box = new CardEvidenceBoxDto("Acme", 0.9, 0.1, 0.1, 0.2, 0.05);
        var front = (await ParseAsync("Rahul Sharma\nAcme Industries Pvt Ltd")) with { Evidence = [box] };
        var back = (await ParseAsync("rahul@acme.in")) with { Evidence = [box with { Text = "rahul" }] };

        var merged = await _service.MergeFrontAndBackAsync(front, back);

        merged.Evidence!.Select(e => e.Side).ShouldBe(["front", "back"]);
    }

    [Theory]
    [InlineData("Landmark: Near ASC", true)]
    [InlineData("Near Forum Mall, Koramangala", true)]
    [InlineData("Plot 18, Sector 62, Noida 201301", true)]
    [InlineData("Address: 12 MG Road", true)]
    [InlineData("Bengaluru, Karnataka", true)]
    [InlineData("4th Main, 2nd Cross", true)]
    [InlineData("Rahul Sharma", false)]
    [InlineData("Delhi Kumar", false)]
    [InlineData("Park Min-jun", false)]
    [InlineData("Main Uddin", false)]
    [InlineData("Name: Rahul Sharma", false)]
    [InlineData("Mobile: +91 98765 43210", false)]
    public void IsAddressLine_separates_addresses_from_names(string line, bool expected)
        => CardLineClassifier.IsAddressLine(line).ShouldBe(expected);

    [Theory]
    [InlineData("Landmark: Near ASC", false)]
    [InlineData("Plot 18 Sector 62", false)]
    [InlineData("Bengaluru 560 001", false)]
    [InlineData("Dr. Anita Rao", true)]
    [InlineData("Zaid Al-Mansoor", true)]
    [InlineData("Rahul", false)]
    public void IsLikelyPersonName_rejects_addresses_and_single_words(string line, bool expected)
        => CardLineClassifier.IsLikelyPersonName(line).ShouldBe(expected);

    [Fact]
    public async Task Several_details_on_one_line_are_all_read()
    {
        var card = await ParseAsync("Omar Khalid\nGeneral Manager\nGulf Star Trading LLC\ninfo@gulfstar.ae | +971 50 123 4567 | www.gulfstar.ae");

        card.Email.ShouldBe("info@gulfstar.ae");
        card.Phone.ShouldBe("+971 50 123 4567");
        card.Website.ShouldBe("www.gulfstar.ae");
        card.FullName.ShouldBe("Omar Khalid");
    }

    [Fact]
    public async Task Labels_on_a_compound_line_are_not_left_behind_as_text()
    {
        var card = await ParseAsync("Sara Nasser\nE: sara@nasser.com  W: www.nasser.com\nT: +971 4 555 0101");

        card.Email.ShouldBe("sara@nasser.com");
        card.Website.ShouldBe("www.nasser.com");
        card.Phone.ShouldNotBeNull();
        card.FullName.ShouldBe("Sara Nasser");
    }

    [Theory]
    [InlineData("@gulfstar_ae")]
    [InlineData("linkedin.com/in/omar-khalid")]
    [InlineData("ISO 9001:2015")]
    [InlineData("All rights reserved")]
    [InlineData("Est. 1998")]
    public async Task Social_handles_certifications_and_footers_never_become_the_name_or_company(string noise)
    {
        var card = await ParseAsync($"{noise}\nOmar Khalid\nGulf Star Trading LLC\nomar@gulfstar.ae");

        card.FullName.ShouldBe("Omar Khalid");
        card.CompanyName.ShouldBe("Gulf Star Trading LLC");
    }

    [Fact]
    public async Task A_social_page_is_not_taken_as_the_website()
    {
        var card = await ParseAsync("Omar Khalid\nGulf Star Trading LLC\nwww.linkedin.com/in/omar\nwww.gulfstar.ae");

        card.Website.ShouldBe("www.gulfstar.ae");
    }

    [Fact]
    public async Task A_company_name_with_a_founding_year_is_kept()
    {
        var card = await ParseAsync("Omar Khalid\nDubai Gold Trading LLC Est. 1998\nomar@dubaigold.ae");

        card.CompanyName.ShouldBe("Dubai Gold Trading LLC Est. 1998");
    }
}
