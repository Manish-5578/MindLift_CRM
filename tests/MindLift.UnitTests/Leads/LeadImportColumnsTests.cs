using MindLift.Shared.Imports;
using Shouldly;

namespace MindLift.UnitTests.Leads;

public sealed class LeadImportColumnsTests
{
    [Theory]
    [InlineData("First name", "firstname")]
    [InlineData("first_name", "firstname")]
    [InlineData("FIRST-NAME", "firstname")]
    [InlineData("  Company Name ", "companyname")]
    [InlineData("E-mail", "email")]
    [InlineData("", "")]
    public void Headers_are_compared_ignoring_case_spaces_and_punctuation(string header, string expected)
    {
        LeadImportColumns.Normalize(header).ShouldBe(expected);
    }

    [Fact]
    public void Columns_are_found_by_any_of_their_names_wherever_they_are()
    {
        var map = LeadImportColumns.Map(["Notes?", "Email", "FIRST NAME", "company_name", "Phone", "Last Name", "Source", "Description"]);

        map["first_name"].ShouldBe(2);
        map["last_name"].ShouldBe(5);
        map["email"].ShouldBe(1);
        map["phone"].ShouldBe(4);
        map["company_name"].ShouldBe(3);
        map["source"].ShouldBe(6);
        map["description"].ShouldBe(7);
    }

    [Fact]
    public void A_column_the_file_does_not_have_is_minus_one()
    {
        var map = LeadImportColumns.Map(["First name"]);

        map["first_name"].ShouldBe(0);
        map["email"].ShouldBe(-1);
        map["company_name"].ShouldBe(-1);
    }

    [Fact]
    public void Company_can_be_called_company_or_company_name()
    {
        LeadImportColumns.Map(["First name", "Company"])["company_name"].ShouldBe(1);
        LeadImportColumns.Map(["First name", "Company name"])["company_name"].ShouldBe(1);
    }

    [Fact]
    public void When_two_columns_match_the_first_one_is_used()
    {
        var map = LeadImportColumns.Map(["First name", "Email", "E-mail"]);

        map["email"].ShouldBe(1);
    }

    [Fact]
    public void Recognizing_shows_what_each_column_will_become_and_which_are_ignored()
    {
        var matches = LeadImportColumns.Recognize(["First name", "Favourite colour", "EMAIL", "email"]);

        matches.Select(m => m.Field).ShouldBe(["first_name", null, "email", null]);
        matches[0].FieldLabel.ShouldBe("First name");
        matches[1].FieldLabel.ShouldBeNull();
        matches[2].FieldLabel.ShouldBe("Email");
        matches[3].Field.ShouldBeNull();
    }

    [Fact]
    public void First_name_is_the_only_required_field()
    {
        LeadImportColumns.RequiredField.ShouldBe("first_name");
    }

    [Fact]
    public void The_template_has_one_heading_per_field_and_imports_cleanly()
    {
        var header = LeadImportColumns.TemplateCsv.TrimEnd().Split(',');

        header.Length.ShouldBe(LeadImportColumns.Fields.Count);
        var map = LeadImportColumns.Map(header);
        map.Values.ShouldAllBe(index => index >= 0);
    }
}
