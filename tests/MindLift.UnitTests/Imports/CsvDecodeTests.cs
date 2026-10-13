using System.Text;
using MindLift.Shared.Imports;
using Shouldly;

namespace MindLift.UnitTests.Imports;

/// <summary>Uploaded files arrive in whatever encoding the spreadsheet program saved them in.</summary>
public sealed class CsvDecodeTests
{
    private const string Sample = "First name,Company\nJosé,Café Müller\n";

    [Fact]
    public void Plain_utf8_is_read_as_is()
        => CsvText.Decode(new UTF8Encoding(false).GetBytes(Sample)).ShouldBe(Sample);

    [Fact]
    public void Utf8_with_a_byte_order_mark_loses_the_mark()
        => CsvText.Decode(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Sample)).ToArray()).ShouldBe(Sample);

    [Fact]
    public void Utf16_files_with_a_mark_are_read_in_both_byte_orders()
    {
        CsvText.Decode(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Sample)).ToArray()).ShouldBe(Sample);
        CsvText.Decode(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(Sample)).ToArray()).ShouldBe(Sample);
    }

    [Fact]
    public void A_latin1_file_keeps_its_accented_letters_instead_of_replacement_marks()
    {
        var text = CsvText.Decode(Encoding.Latin1.GetBytes(Sample));

        text.ShouldBe(Sample);
        text.ShouldNotContain("�");
    }

    [Fact]
    public void Arabic_utf8_text_is_not_mistaken_for_latin1()
        => CsvText.Decode(Encoding.UTF8.GetBytes("الاسم,الشركة\nنادية,شركة")).ShouldContain("نادية");

    [Fact]
    public void An_empty_file_is_an_empty_string()
        => CsvText.Decode([]).ShouldBe(string.Empty);

    [Fact]
    public void The_decoded_text_parses_into_rows()
        => CsvText.Parse(CsvText.Decode(Encoding.Latin1.GetBytes(Sample)))[1].ShouldBe(["José", "Café Müller"]);
}
