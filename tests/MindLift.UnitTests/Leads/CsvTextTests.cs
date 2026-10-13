using MindLift.Shared.Imports;
using Shouldly;

namespace MindLift.UnitTests.Leads;

public sealed class CsvTextTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("line1\r\nline2", "\"line1\r\nline2\"")]
    public void Values_are_quoted_only_when_they_need_to_be(string value, string expected)
    {
        CsvText.Escape(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("=HYPERLINK(\"http://evil\",\"x\")", "\"'=HYPERLINK(\"\"http://evil\"\",\"\"x\"\")\"")]
    [InlineData("@SUM(A1:A9)", "'@SUM(A1:A9)")]
    [InlineData("+cmd|' /C calc'!A0", "'+cmd|' /C calc'!A0")]
    [InlineData("-2+3+cmd|' /C calc'!A0", "'-2+3+cmd|' /C calc'!A0")]
    [InlineData("\tTab first", "'\tTab first")]
    public void Values_that_a_spreadsheet_would_run_as_a_formula_get_a_leading_apostrophe(string value, string expected)
    {
        CsvText.Escape(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData("+971 50 123 4567")]
    [InlineData("+971501234567")]
    [InlineData("(04) 555-1234")]
    [InlineData("-12.5")]
    [InlineData("2026-10-09T08:00:00.0000000Z")]
    [InlineData("Ahmed")]
    [InlineData("a=b")]
    public void Phone_numbers_plain_numbers_and_normal_text_are_left_alone(string value)
    {
        CsvText.Escape(value).ShouldBe(value);
    }

    [Fact]
    public void Quoted_fields_with_commas_quotes_and_line_breaks_are_read_back_whole()
    {
        var csv = "name,note\n\"Jr, Ben\",\"said \"\"hi\"\"\"\nAna,\"two\nlines\"\n";

        var rows = CsvText.Parse(csv);

        rows.Count.ShouldBe(3);
        rows[1].ShouldBe(["Jr, Ben", "said \"hi\""]);
        rows[2].ShouldBe(["Ana", "two\nlines"]);
    }

    [Fact]
    public void A_written_row_is_read_back_unchanged()
    {
        var values = new[] { "Ana", "Q, \"the\" best", "plain" };
        var line = string.Join(",", values.Select(CsvText.Escape));

        CsvText.Parse(line + "\n")[0].ShouldBe(values);
    }

    [Fact]
    public void Blank_lines_are_skipped()
    {
        CsvText.Parse("a,b\n\n1,2\n\n").Count.ShouldBe(2);
    }
}
