using System.Text.Json;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using MindLift.UI.Components;
using Shouldly;

namespace MindLift.UITests;

public sealed class FilterConverterTests
{
    private static FieldDto Field(string key, FieldType type) => new(key, key, null, type, false, false, false, true, null, null);

    private static readonly IReadOnlyList<FieldDto> Fields =
    [
        Field("first_name", FieldType.Text),
        Field("budget", FieldType.Currency),
        Field("created_at", FieldType.DateTime),
        Field("status", FieldType.Picklist),
        Field("owner_id", FieldType.User),
        Field("is_vip", FieldType.Boolean),
    ];

    private static FilterGroup Build(params FilterRow[] rows) => FilterConverter.ToGroup(rows, Fields);

    private static string Json(FilterCondition c) => c.Value is { } v ? v.GetRawText() : "null";

    [Fact]
    public void A_text_row_becomes_a_trimmed_string_value()
    {
        var group = Build(new FilterRow { Field = "first_name", Op = FilterOperator.Contains, Text = "  sam " });

        var c = group.Conditions.ShouldHaveSingleItem();
        c.Field.ShouldBe("first_name");
        c.Op.ShouldBe(FilterOperator.Contains);
        Json(c).ShouldBe("\"sam\"");
    }

    [Fact]
    public void A_number_row_becomes_a_number()
    {
        var group = Build(new FilterRow { Field = "budget", Op = FilterOperator.Gte, Number = 1500000m });

        Json(group.Conditions.Single()).ShouldBe("1500000");
    }

    [Fact]
    public void A_numeric_between_row_becomes_from_and_to()
    {
        var group = Build(new FilterRow { Field = "budget", Op = FilterOperator.Between, Number = 10, Number2 = 20 });

        Json(group.Conditions.Single()).ShouldBe("{\"from\":10,\"to\":20}");
    }

    [Fact]
    public void A_date_row_becomes_an_iso_day()
    {
        var group = Build(new FilterRow { Field = "created_at", Op = FilterOperator.Gte, From = new DateTime(2026, 10, 9, 15, 30, 0) });

        Json(group.Conditions.Single()).ShouldBe("\"2026-10-09\"");
    }

    [Fact]
    public void A_date_between_row_becomes_from_and_to_days()
    {
        var group = Build(new FilterRow { Field = "created_at", Op = FilterOperator.Between, From = new DateTime(2026, 1, 1), To = new DateTime(2026, 2, 1) });

        Json(group.Conditions.Single()).ShouldBe("{\"from\":\"2026-01-01\",\"to\":\"2026-02-01\"}");
    }

    [Fact]
    public void A_date_preset_row_sends_the_preset_code()
    {
        var group = Build(new FilterRow { Field = "created_at", Op = FilterOperator.DatePreset, Preset = "ThisMonth" });

        Json(group.Conditions.Single()).ShouldBe("\"ThisMonth\"");
    }

    [Fact]
    public void A_picklist_row_becomes_a_sorted_array()
    {
        var group = Build(new FilterRow { Field = "status", Op = FilterOperator.In, Choices = ["working", "new"] });

        Json(group.Conditions.Single()).ShouldBe("[\"new\",\"working\"]");
    }

    [Fact]
    public void A_user_row_keeps_at_me()
    {
        var group = Build(new FilterRow { Field = "owner_id", Op = FilterOperator.Eq, Text = "@me" });

        Json(group.Conditions.Single()).ShouldBe("\"@me\"");
    }

    [Fact]
    public void A_yes_no_row_becomes_a_boolean()
    {
        Json(Build(new FilterRow { Field = "is_vip", Op = FilterOperator.Eq, Flag = true }).Conditions.Single()).ShouldBe("true");
        Json(Build(new FilterRow { Field = "is_vip", Op = FilterOperator.Eq, Flag = false }).Conditions.Single()).ShouldBe("false");
    }

    [Theory]
    [InlineData(FilterOperator.IsEmpty)]
    [InlineData(FilterOperator.IsNotEmpty)]
    public void Is_empty_rows_need_no_value(FilterOperator op)
    {
        var group = Build(new FilterRow { Field = "first_name", Op = op });

        var c = group.Conditions.ShouldHaveSingleItem();
        c.Value.ShouldBeNull();
    }

    [Fact]
    public void Rows_that_are_not_finished_are_left_out()
    {
        var group = Build(
            new FilterRow { Field = "first_name", Op = FilterOperator.Contains, Text = "  " },
            new FilterRow { Field = "budget", Op = FilterOperator.Gte },
            new FilterRow { Field = "budget", Op = FilterOperator.Between, Number = 5 },
            new FilterRow { Field = "created_at", Op = FilterOperator.Between, From = new DateTime(2026, 1, 1) },
            new FilterRow { Field = "status", Op = FilterOperator.In },
            new FilterRow { Field = "is_vip", Op = FilterOperator.Eq },
            new FilterRow { Field = "created_at", Op = FilterOperator.DatePreset },
            new FilterRow { Field = "first_name", Op = FilterOperator.Eq, Text = "Sam" });

        group.Conditions.Select(c => c.Field).ShouldBe(["first_name"]);
    }

    [Fact]
    public void A_row_for_a_field_that_no_longer_exists_is_left_out()
    {
        Build(new FilterRow { Field = "deleted_field", Op = FilterOperator.Eq, Text = "x" }).Conditions.ShouldBeEmpty();
    }

    [Fact]
    public void The_logic_and_nested_groups_are_carried_through()
    {
        var nested = new FilterGroup { Logic = "or", Conditions = [new FilterCondition { Field = "status", Op = FilterOperator.Eq }] };

        var group = FilterConverter.ToGroup([new FilterRow { Field = "first_name", Op = FilterOperator.Eq, Text = "Sam" }], Fields, "or", [nested]);

        group.Logic.ShouldBe("or");
        group.Groups.ShouldHaveSingleItem().ShouldBeSameAs(nested);
    }

    public static IEnumerable<object[]> RoundTrips()
    {
        yield return [new FilterRow { Field = "first_name", Op = FilterOperator.StartsWith, Text = "Sam" }];
        yield return [new FilterRow { Field = "budget", Op = FilterOperator.Gt, Number = 99.5m }];
        yield return [new FilterRow { Field = "budget", Op = FilterOperator.Between, Number = 1, Number2 = 9 }];
        yield return [new FilterRow { Field = "created_at", Op = FilterOperator.Lte, From = new DateTime(2026, 3, 4) }];
        yield return [new FilterRow { Field = "created_at", Op = FilterOperator.Between, From = new DateTime(2026, 3, 4), To = new DateTime(2026, 4, 4) }];
        yield return [new FilterRow { Field = "created_at", Op = FilterOperator.DatePreset, Preset = "LastQuarter" }];
        yield return [new FilterRow { Field = "status", Op = FilterOperator.NotIn, Choices = ["new", "lost"] }];
        yield return [new FilterRow { Field = "owner_id", Op = FilterOperator.Eq, Text = "@me" }];
        yield return [new FilterRow { Field = "is_vip", Op = FilterOperator.Eq, Flag = true }];
    }

    [Theory]
    [MemberData(nameof(RoundTrips))]
    public void A_condition_loads_back_into_the_same_row(FilterRow original)
    {
        var field = Fields.Single(f => f.Key == original.Field);
        var condition = FilterConverter.ToCondition(original, field)!;

        var loaded = FilterConverter.ToRow(condition, field);

        loaded.Field.ShouldBe(original.Field);
        loaded.Op.ShouldBe(original.Op);
        loaded.Text.ShouldBe(original.Text);
        loaded.Number.ShouldBe(original.Number);
        loaded.Number2.ShouldBe(original.Number2);
        loaded.From.ShouldBe(original.From);
        loaded.To.ShouldBe(original.To);
        loaded.Preset.ShouldBe(original.Preset);
        loaded.Flag.ShouldBe(original.Flag);
        loaded.Choices.OrderBy(c => c).ShouldBe(original.Choices.OrderBy(c => c));
    }

    [Fact]
    public void A_condition_without_a_value_loads_as_an_empty_row()
    {
        var row = FilterConverter.ToRow(new FilterCondition { Field = "first_name", Op = FilterOperator.IsEmpty }, Fields[0]);

        row.Op.ShouldBe(FilterOperator.IsEmpty);
        row.Text.ShouldBeNull();
    }
}
