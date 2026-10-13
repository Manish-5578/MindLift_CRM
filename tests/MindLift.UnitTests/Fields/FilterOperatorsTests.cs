using System.Reflection;
using System.Text.Json;
using MindLift.Application.Common.Fields;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.UnitTests.Fields;

/// <summary>The operators the filter builder offers must all be accepted by the server's filter compiler.</summary>
public sealed class FilterOperatorsTests
{
    private static readonly FilterContext Context = new(
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai"),
        new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc),
        DayOfWeek.Monday,
        1,
        Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private static readonly MethodInfo CompileMethod = typeof(FilterCompiler).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(FilterCompiler.Compile) && m.IsGenericMethodDefinition);

    public static IEnumerable<object[]> AllOfferedCombinations()
    {
        foreach (var entity in SystemFields.Entities)
        {
            foreach (var field in entity.SystemFields.Where(f => f.Filterable))
            {
                foreach (var op in FilterOperators.For(field.Type))
                {
                    yield return [entity.Key, field.Key, op];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllOfferedCombinations))]
    public void Every_operator_the_screen_offers_compiles_for_that_field(string entityKey, string fieldKey, FilterOperator op)
    {
        var entity = SystemFields.Get(entityKey);
        var field = entity.SystemFields.Single(f => f.Key == fieldKey);
        var group = new FilterGroup { Conditions = [new FilterCondition { Field = fieldKey, Op = op, Value = SampleValue(field.Type, op) }] };

        var compile = CompileMethod.MakeGenericMethod(entity.ClrType);
        try
        {
            compile.Invoke(null, [group, entity.SystemFields, Context]).ShouldNotBeNull();
        }
        catch (TargetInvocationException ex)
        {
            throw new ShouldAssertException($"{entityKey}.{fieldKey} {op} was offered but does not compile: {ex.InnerException?.Message}");
        }
    }

    [Fact]
    public void Every_field_type_offers_at_least_one_operator()
    {
        foreach (var type in Enum.GetValues<FieldType>())
        {
            FilterOperators.For(type).ShouldNotBeEmpty($"{type} has no operators");
        }
    }

    [Fact]
    public void Every_operator_has_a_label_that_is_not_the_enum_name()
    {
        foreach (var op in Enum.GetValues<FilterOperator>())
        {
            FilterOperators.Label(op).ShouldNotBe(op.ToString());
        }
    }

    [Fact]
    public void The_date_presets_offered_are_the_ones_the_server_knows()
    {
        var known = Enum.GetNames<MindLift.Shared.Enums.DatePreset>();

        foreach (var (code, _) in FilterOperators.DatePresets)
        {
            known.ShouldContain(code);
        }
    }

    [Fact]
    public void Only_is_empty_and_is_not_empty_need_no_value()
    {
        Enum.GetValues<FilterOperator>().Where(FilterOperators.NeedsNoValue)
            .ShouldBe([FilterOperator.IsEmpty, FilterOperator.IsNotEmpty], ignoreOrder: true);
    }

    private static JsonElement? SampleValue(FieldType type, FilterOperator op)
    {
        if (FilterOperators.NeedsNoValue(op))
        {
            return null;
        }

        var isDate = type is FieldType.Date or FieldType.DateTime;
        var isNumber = type is FieldType.Number or FieldType.Decimal or FieldType.Currency or FieldType.Percent;

        if (op == FilterOperator.DatePreset)
        {
            return JsonSerializer.SerializeToElement("ThisMonth");
        }

        if (op == FilterOperator.Between)
        {
            return isDate
                ? JsonSerializer.SerializeToElement(new { from = "2026-01-01", to = "2026-02-01" })
                : JsonSerializer.SerializeToElement(new { from = 1, to = 2 });
        }

        object single = isDate ? "2026-01-01"
            : isNumber ? 5
            : type == FieldType.Boolean ? true
            : type is FieldType.User ? "@me"
            : type is FieldType.Lookup ? Guid.NewGuid().ToString()
            : "a";

        return op is FilterOperator.In or FilterOperator.NotIn
            ? JsonSerializer.SerializeToElement(new[] { single })
            : JsonSerializer.SerializeToElement(single);
    }
}
