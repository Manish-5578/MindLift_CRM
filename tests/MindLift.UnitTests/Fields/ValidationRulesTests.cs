using System.Text.Json;
using MindLift.Application.Common.Fields;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.UnitTests.Fields;

public sealed class CustomFieldRulesTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static readonly IReadOnlyList<CustomFieldRule> Rules =
    [
        new("cf_budget", "Budget", FieldType.Currency, IsRequired: true, OptionValues: []),
        new("cf_bedrooms", "Bedrooms", FieldType.Number, false, []),
        new("cf_type", "Property type", FieldType.Picklist, false, ["villa", "apartment"]),
        new("cf_tags", "Tags", FieldType.MultiPicklist, false, ["hot", "cold"]),
        new("cf_expiry", "Expiry", FieldType.Date, false, []),
    ];

    [Theory]
    [InlineData("cf_budget", true)]
    [InlineData("cf_bedrooms", true)]
    [InlineData("cf_budget_x", true)]
    [InlineData("Budget", false)]
    [InlineData("cf_", false)]
    [InlineData("cf_1abc", false)]
    public void Keys_must_start_with_cf_and_use_snake_case(string key, bool valid)
    {
        CustomFieldRules.IsValidKey(key).ShouldBe(valid);
    }

    [Fact]
    public void A_valid_set_of_values_has_no_errors()
    {
        var values = new Dictionary<string, JsonElement?>
        {
            ["cf_budget"] = Json("2500000"),
            ["cf_type"] = Json("\"villa\""),
            ["cf_tags"] = Json("[\"hot\"]"),
            ["cf_expiry"] = Json("\"2027-03-31\""),
        };

        CustomFieldRules.ValidateValues(Rules, values).ShouldBeEmpty();
    }

    [Fact]
    public void Missing_required_values_are_reported()
    {
        var errors = CustomFieldRules.ValidateValues(Rules, new Dictionary<string, JsonElement?>());

        errors.ShouldContainKey("cf_budget");
    }

    [Fact]
    public void Wrong_types_and_unknown_options_are_reported_per_field()
    {
        var values = new Dictionary<string, JsonElement?>
        {
            ["cf_budget"] = Json("\"lots\""),
            ["cf_type"] = Json("\"castle\""),
            ["cf_tags"] = Json("[\"hot\", \"warm\"]"),
            ["cf_expiry"] = Json("\"31/03/2027\""),
        };

        var errors = CustomFieldRules.ValidateValues(Rules, values);

        errors.Keys.ShouldBe(["cf_budget", "cf_type", "cf_tags", "cf_expiry"], ignoreOrder: true);
    }

    [Fact]
    public void Unknown_keys_are_rejected()
    {
        var values = new Dictionary<string, JsonElement?>
        {
            ["cf_budget"] = Json("1"),
            ["cf_surprise"] = Json("\"x\""),
        };

        CustomFieldRules.ValidateValues(Rules, values).ShouldContainKey("cf_surprise");
    }
}

public sealed class PipelineRulesTests
{
    [Fact]
    public void A_standard_pipeline_is_valid()
    {
        PipelineRules.Validate([("Open", 10), ("Open", 60), ("Won", 100), ("Lost", 0)]).ShouldBeNull();
    }

    [Fact]
    public void A_pipeline_needs_an_open_stage()
    {
        PipelineRules.Validate([("Won", 100), ("Lost", 0)]).ShouldNotBeNull();
    }

    [Fact]
    public void A_pipeline_needs_exactly_one_won_and_one_lost()
    {
        PipelineRules.Validate([("Open", 10), ("Won", 100)]).ShouldNotBeNull();
        PipelineRules.Validate([("Open", 10), ("Won", 100), ("Won", 100), ("Lost", 0)]).ShouldNotBeNull();
    }

    [Fact]
    public void Won_and_lost_must_come_after_every_open_stage()
    {
        PipelineRules.Validate([("Open", 10), ("Won", 100), ("Open", 50), ("Lost", 0)]).ShouldNotBeNull();
    }

    [Fact]
    public void Probability_must_be_between_zero_and_one_hundred()
    {
        PipelineRules.Validate([("Open", 120), ("Won", 100), ("Lost", 0)]).ShouldNotBeNull();
    }
}
