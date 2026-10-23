using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.UnitTests.Documents;

/// <summary>Which record fields a value read from a document can be saved to.</summary>
public sealed class DocumentValueTypesTests
{
    [Theory]
    [InlineData(DocumentValueTypes.Text, DocumentValueTypes.Text, true)]
    [InlineData(DocumentValueTypes.Date, DocumentValueTypes.Date, true)]
    [InlineData(DocumentValueTypes.Date, DocumentValueTypes.Text, true)]       // a date can be kept as text
    [InlineData(DocumentValueTypes.Text, DocumentValueTypes.Date, false)]      // free text cannot become a date
    [InlineData(DocumentValueTypes.Amount, DocumentValueTypes.Number, true)]
    [InlineData(DocumentValueTypes.Number, DocumentValueTypes.Amount, true)]
    [InlineData(DocumentValueTypes.Amount, DocumentValueTypes.Email, false)]
    [InlineData(DocumentValueTypes.Email, DocumentValueTypes.Email, true)]
    [InlineData(DocumentValueTypes.Email, DocumentValueTypes.Text, true)]
    [InlineData(DocumentValueTypes.Text, DocumentValueTypes.Email, false)]
    [InlineData(DocumentValueTypes.Phone, DocumentValueTypes.Phone, true)]
    [InlineData(DocumentValueTypes.Id, DocumentValueTypes.Text, true)]
    [InlineData(DocumentValueTypes.Id, DocumentValueTypes.Number, false)]
    public void A_value_fits_a_record_field_of_the_same_kind_or_of_text(string document, string target, bool expected)
    {
        DocumentValueTypes.Compatible(document, target).ShouldBe(expected);
    }

    [Fact]
    public void Every_kind_of_value_fits_a_field_of_its_own_kind()
    {
        foreach (var type in DocumentValueTypes.All)
        {
            DocumentValueTypes.Compatible(type, type).ShouldBeTrue(type);
        }
    }
}
