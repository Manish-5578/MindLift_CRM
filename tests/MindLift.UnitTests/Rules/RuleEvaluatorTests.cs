using MindLift.Application.Rules;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Rules;

public sealed class RuleEvaluatorTests
{
    private static Dictionary<string, object?> Facts(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Theory]
    [InlineData(RuleOperators.Eq, "Won", true)]
    [InlineData(RuleOperators.Eq, "won", true)]
    [InlineData(RuleOperators.Eq, "Lost", false)]
    [InlineData(RuleOperators.Neq, "Lost", true)]
    [InlineData(RuleOperators.Neq, "Won", false)]
    [InlineData(RuleOperators.Contains, "on", true)]
    [InlineData(RuleOperators.NotContains, "zz", true)]
    [InlineData(RuleOperators.StartsWith, "Wo", true)]
    [InlineData(RuleOperators.EndsWith, "on", true)]
    [InlineData(RuleOperators.EndsWith, "xx", false)]
    public void Text_comparisons_ignore_upper_and_lower_case(string op, string value, bool expected)
    {
        RuleEvaluator.Evaluate(RuleNode.When("stage", op, value), Facts(("stage", "Won"))).ShouldBe(expected);
    }

    [Theory]
    [InlineData(RuleOperators.GreaterThan, "999", true)]
    [InlineData(RuleOperators.GreaterThan, "1000", false)]
    [InlineData(RuleOperators.GreaterOrEqual, "1000", true)]
    [InlineData(RuleOperators.LessThan, "1000.5", true)]
    [InlineData(RuleOperators.LessOrEqual, "999", false)]
    [InlineData(RuleOperators.Eq, "1000.00", true)]
    public void Numbers_compare_as_numbers_not_as_text(string op, string value, bool expected)
    {
        // As text "1000" < "999"; as numbers it is greater.
        RuleEvaluator.Evaluate(RuleNode.When("amount", op, value), Facts(("amount", 1000m))).ShouldBe(expected);
    }

    [Fact]
    public void Dates_compare_as_dates()
    {
        var facts = Facts(("closeDate", new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc)));

        RuleEvaluator.Evaluate(RuleNode.When("closeDate", RuleOperators.GreaterThan, "2026-03-01"), facts).ShouldBeTrue();
        RuleEvaluator.Evaluate(RuleNode.When("closeDate", RuleOperators.LessThan, "2026-03-01"), facts).ShouldBeFalse();
    }

    [Fact]
    public void Empty_checks_treat_missing_null_and_blank_the_same()
    {
        RuleEvaluator.Evaluate(RuleNode.When("email", RuleOperators.IsEmpty), Facts()).ShouldBeTrue();
        RuleEvaluator.Evaluate(RuleNode.When("email", RuleOperators.IsEmpty), Facts(("email", null))).ShouldBeTrue();
        RuleEvaluator.Evaluate(RuleNode.When("email", RuleOperators.IsEmpty), Facts(("email", "  "))).ShouldBeTrue();
        RuleEvaluator.Evaluate(RuleNode.When("email", RuleOperators.IsNotEmpty), Facts(("email", "a@b.test"))).ShouldBeTrue();
    }

    [Fact]
    public void In_and_not_in_check_a_list()
    {
        var facts = Facts(("country", "ae"));

        RuleEvaluator.Evaluate(RuleNode.WhenIn("country", RuleOperators.In, "AE", "SA"), facts).ShouldBeTrue();
        RuleEvaluator.Evaluate(RuleNode.WhenIn("country", RuleOperators.In, "US"), facts).ShouldBeFalse();
        RuleEvaluator.Evaluate(RuleNode.WhenIn("country", RuleOperators.NotIn, "US"), facts).ShouldBeTrue();
    }

    [Fact]
    public void Groups_combine_with_and_or_and_can_be_nested()
    {
        var rule = RuleNode.All(
            RuleNode.When("amount", RuleOperators.GreaterThan, "500"),
            RuleNode.Any(
                RuleNode.When("stage", RuleOperators.Eq, "Won"),
                RuleNode.When("stage", RuleOperators.Eq, "Negotiation")));

        RuleEvaluator.Evaluate(rule, Facts(("amount", 600), ("stage", "Negotiation"))).ShouldBeTrue();
        RuleEvaluator.Evaluate(rule, Facts(("amount", 400), ("stage", "Won"))).ShouldBeFalse();
        RuleEvaluator.Evaluate(rule, Facts(("amount", 600), ("stage", "Lost"))).ShouldBeFalse();
    }

    [Fact]
    public void Field_names_ignore_upper_and_lower_case_and_unknown_fields_read_as_empty()
    {
        RuleEvaluator.Evaluate(RuleNode.When("STAGE", RuleOperators.Eq, "won"), Facts(("stage", "Won"))).ShouldBeTrue();
        RuleEvaluator.Evaluate(RuleNode.When("nothing", RuleOperators.Eq, "x"), Facts(("stage", "Won"))).ShouldBeFalse();
    }

    [Fact]
    public void A_rule_that_is_not_valid_never_matches()
    {
        RuleEvaluator.Evaluate(null, Facts()).ShouldBeFalse();
        RuleEvaluator.Evaluate(new RuleNode("group", "and", []), Facts()).ShouldBeFalse();
        RuleEvaluator.Evaluate(RuleNode.When("a", "regex", ".*"), Facts(("a", "x"))).ShouldBeFalse();
        RuleEvaluator.Evaluate(new RuleNode("condition", Field: "a", Operator: "eq", Value: null), Facts(("a", "x"))).ShouldBeFalse();
    }

    [Fact]
    public void Validation_explains_what_is_wrong()
    {
        RuleEvaluator.Validate(null).ShouldNotBeEmpty();
        RuleEvaluator.Validate(new RuleNode("banana")).ShouldNotBeEmpty();
        RuleEvaluator.Validate(new RuleNode("group", "xor", [RuleNode.When("a", "eq", "1")])).ShouldContain(e => e.Contains("'and' or 'or'"));
        RuleEvaluator.Validate(RuleNode.When("", "eq", "1")).ShouldContain(e => e.Contains("field"));
        RuleEvaluator.Validate(RuleNode.When("a", "eq")).ShouldContain(e => e.Contains("value"));
        RuleEvaluator.Validate(RuleNode.WhenIn("a", RuleOperators.In)).ShouldContain(e => e.Contains("at least one value"));
        RuleEvaluator.Validate(RuleNode.When("a", RuleOperators.IsEmpty)).ShouldBeEmpty();
    }

    [Fact]
    public void Validation_limits_the_fields_a_rule_may_read()
    {
        RuleEvaluator.Validate(RuleNode.When("amount", "gt", "1"), ["amount", "stage"]).ShouldBeEmpty();
        RuleEvaluator.Validate(RuleNode.When("password_hash", "eq", "x"), ["amount", "stage"]).ShouldContain(e => e.Contains("password_hash"));
    }

    [Fact]
    public void Validation_caps_depth_size_and_value_length_so_a_rule_cannot_be_used_to_overload_the_server()
    {
        var deep = RuleNode.When("a", "eq", "1");
        for (var i = 0; i < RuleEvaluator.MaxDepth + 1; i++)
        {
            deep = RuleNode.All(deep);
        }

        RuleEvaluator.Validate(deep).ShouldContain(e => e.Contains("nested"));
        RuleEvaluator.Evaluate(deep, Facts(("a", "1"))).ShouldBeFalse();

        var wide = RuleNode.All(Enumerable.Range(0, RuleEvaluator.MaxNodes + 5).Select(i => RuleNode.When("a", "eq", "1")).ToArray());
        RuleEvaluator.Validate(wide).ShouldContain(e => e.Contains("at most"));

        RuleEvaluator.Validate(RuleNode.When("a", "eq", new string('x', RuleEvaluator.MaxValueLength + 1))).ShouldContain(e => e.Contains("characters"));
    }

    [Fact]
    public void Special_characters_in_a_value_are_plain_text_not_a_pattern()
    {
        RuleEvaluator.Evaluate(RuleNode.When("name", RuleOperators.Contains, ".*"), Facts(("name", "Acme"))).ShouldBeFalse();
        RuleEvaluator.Evaluate(RuleNode.When("name", RuleOperators.Contains, "(a+)+$"), Facts(("name", new string('a', 5000) + "!"))).ShouldBeFalse();
    }
}
