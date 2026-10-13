using System.Text.Json;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.UnitTests.Fields;

/// <summary>Plan 4.6.4: the shared filter model compiles to correct expressions for system fields.</summary>
public sealed class FilterCompilerTests
{
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly FilterContext Context = new(
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai"),
        new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc),
        DayOfWeek.Monday,
        1,
        Me);

    private static readonly IReadOnlyList<FieldDef> LeadFields = MindLift.Application.Common.Fields.SystemFields.Get("leads").SystemFields;

    private static List<Lead> Leads() =>
    [
        new Lead { FirstName = "Ahmed", LastName = "Khan", Status = "new", OwnerId = Me, Email = "ahmed@mail.test", CreatedAt = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc) },
        new Lead { FirstName = "sara", LastName = "Ali", Status = "qualified", OwnerId = Other, Email = null, CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc) },
        new Lead { FirstName = "Omar", LastName = "Nasser", Status = "new", OwnerId = Other, Email = "", CreatedAt = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc) },
    ];

    private static FilterCondition Cond(string field, FilterOperator op, object? value = null) => new()
    {
        Field = field,
        Op = op,
        Value = value is null ? null : JsonSerializer.SerializeToElement(value),
    };

    private static List<string> Run(FilterGroup group)
    {
        var predicate = FilterCompiler.Compile<Lead>(group, LeadFields, Context).Compile();
        return Leads().Where(predicate).Select(l => (l.FirstName + " " + l.LastName).ToLowerInvariant()).ToList();
    }

    [Fact]
    public void Equality_on_a_picklist_code()
    {
        var group = new FilterGroup { Conditions = [Cond("status", FilterOperator.Eq, "new")] };

        Run(group).ShouldBe(["ahmed khan", "omar nasser"]);
    }

    [Fact]
    public void Contains_is_case_insensitive()
    {
        var group = new FilterGroup { Conditions = [Cond("first_name", FilterOperator.Contains, "AH")] };

        Run(group).ShouldBe(["ahmed khan"]);
    }

    [Fact]
    public void Is_empty_treats_null_and_blank_text_as_empty()
    {
        var group = new FilterGroup { Conditions = [Cond("email", FilterOperator.IsEmpty)] };

        Run(group).ShouldBe(["sara ali", "omar nasser"]);
    }

    [Fact]
    public void Contains_and_starts_with_skip_missing_values_instead_of_failing()
    {
        // Sara has no email at all. Running the filter in memory must not throw on her.
        Run(new FilterGroup { Conditions = [Cond("email", FilterOperator.Contains, "mail")] }).ShouldBe(["ahmed khan"]);
        Run(new FilterGroup { Conditions = [Cond("email", FilterOperator.StartsWith, "ahmed")] }).ShouldBe(["ahmed khan"]);
    }

    [Fact]
    public void Does_not_contain_includes_records_with_no_value()
    {
        Run(new FilterGroup { Conditions = [Cond("email", FilterOperator.NotContains, "mail")] }).ShouldBe(["sara ali", "omar nasser"]);
    }

    [Theory]
    [InlineData("owner_id")]
    [InlineData("created_at")]
    public void A_field_that_can_never_be_null_is_never_empty(string field)
    {
        var group = new FilterGroup { Conditions = [Cond(field, FilterOperator.IsEmpty)] };

        Run(group).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("owner_id")]
    [InlineData("created_at")]
    public void A_field_that_can_never_be_null_is_always_not_empty(string field)
    {
        var group = new FilterGroup { Conditions = [Cond(field, FilterOperator.IsNotEmpty)] };

        Run(group).ShouldBe(["ahmed khan", "sara ali", "omar nasser"]);
    }

    [Fact]
    public void In_matches_any_value_in_the_list()
    {
        var group = new FilterGroup { Conditions = [Cond("status", FilterOperator.In, new[] { "qualified", "missing" })] };

        Run(group).ShouldBe(["sara ali"]);
    }

    [Fact]
    public void At_me_resolves_to_the_current_user()
    {
        var group = new FilterGroup { Conditions = [Cond("owner_id", FilterOperator.Eq, "@me")] };

        Run(group).ShouldBe(["ahmed khan"]);
    }

    [Fact]
    public void Or_groups_combine_with_or()
    {
        var group = new FilterGroup
        {
            Logic = "or",
            Conditions =
            [
                Cond("status", FilterOperator.Eq, "qualified"),
                Cond("owner_id", FilterOperator.Eq, "@me"),
            ],
        };

        Run(group).ShouldBe(["ahmed khan", "sara ali"]);
    }

    [Fact]
    public void Date_preset_this_month_uses_the_tenant_time_zone()
    {
        var group = new FilterGroup { Conditions = [Cond("created_at", FilterOperator.DatePreset, "ThisMonth")] };

        Run(group).ShouldBe(["ahmed khan", "omar nasser"]);
    }

    [Fact]
    public void Unknown_fields_are_rejected()
    {
        var group = new FilterGroup { Conditions = [Cond("no_such_field", FilterOperator.Eq, "x")] };

        var error = Should.Throw<ValidationException>(() => FilterCompiler.Compile<Lead>(group, LeadFields, Context));
        error.Errors.ShouldContainKey("no_such_field");
    }

    [Fact]
    public void Text_operators_are_rejected_on_non_text_fields()
    {
        var group = new FilterGroup { Conditions = [Cond("owner_id", FilterOperator.Contains, "x")] };

        Should.Throw<ValidationException>(() => FilterCompiler.Compile<Lead>(group, LeadFields, Context));
    }

    [Fact]
    public void Groups_nested_deeper_than_three_levels_are_rejected()
    {
        var deep = new FilterGroup { Conditions = [Cond("status", FilterOperator.Eq, "new")] };
        for (var i = 0; i < 3; i++)
        {
            deep = new FilterGroup { Groups = [deep] };
        }

        Should.Throw<ValidationException>(() => FilterCompiler.Compile<Lead>(deep, LeadFields, Context));
    }

    [Fact]
    public void An_empty_filter_matches_everything()
    {
        Run(new FilterGroup()).Count.ShouldBe(3);
    }
}
