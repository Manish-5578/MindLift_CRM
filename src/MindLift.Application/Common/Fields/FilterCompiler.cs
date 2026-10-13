using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Time;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;

namespace MindLift.Application.Common.Fields;

/// <summary>What the compiler needs from the request: time zone, week start and the current user (for "@me").</summary>
public sealed record FilterContext(TimeZoneInfo Zone, DateTime NowUtc, DayOfWeek WeekStart, int FiscalYearStartMonth, Guid? CurrentUserId);

/// <summary>
/// Compiles the shared filter model into an expression tree (plan 4.6.4).
/// System fields become property accesses; custom fields become crm_cf_text / crm_cf_numeric calls on the jsonb document.
/// Unknown fields and operators that do not fit the field type are rejected with a validation error.
/// </summary>
public static class FilterCompiler
{
    public static Expression<Func<T, bool>> Compile<T>(FilterGroup group, IReadOnlyList<FieldDef> fields, FilterContext context)
    {
        var parameter = Expression.Parameter(typeof(T), "e");
        var body = Build(group, parameter, fields, context, depth: 1);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression Build(FilterGroup group, ParameterExpression parameter, IReadOnlyList<FieldDef> fields, FilterContext context, int depth)
    {
        if (depth > FilterGroup.MaxDepth)
        {
            throw Invalid("filter", $"Filters can nest at most {FilterGroup.MaxDepth} levels deep.");
        }

        var parts = new List<Expression>();
        parts.AddRange(group.Conditions.Select(c => Condition(c, parameter, fields, context)));
        parts.AddRange(group.Groups.Select(g => Build(g, parameter, fields, context, depth + 1)));

        if (parts.Count == 0)
        {
            return Expression.Constant(true);
        }

        var isOr = string.Equals(group.Logic, "or", StringComparison.OrdinalIgnoreCase);
        return parts.Aggregate((left, right) => isOr ? Expression.OrElse(left, right) : Expression.AndAlso(left, right));
    }

    private static Expression Condition(FilterCondition condition, ParameterExpression parameter, IReadOnlyList<FieldDef> fields, FilterContext context)
    {
        var field = fields.FirstOrDefault(f => f.Key == condition.Field)
            ?? throw Invalid(condition.Field, $"Unknown field '{condition.Field}'.");

        if (!field.Filterable)
        {
            throw Invalid(field.Key, $"Field '{field.Key}' cannot be used in filters.");
        }

        var (member, memberType) = Member(field, parameter);

        if (condition.Op == FilterOperator.DatePreset)
        {
            return DatePresetCondition(field, member, condition.Value, context);
        }

        return condition.Op switch
        {
            FilterOperator.IsEmpty => IsEmpty(member, memberType),
            FilterOperator.IsNotEmpty => Expression.Not(IsEmpty(member, memberType)),
            FilterOperator.Between => Between(field, member, memberType, condition.Value, context),
            FilterOperator.In => Membership(field, member, memberType, condition.Value, context, negate: false),
            FilterOperator.NotIn => Membership(field, member, memberType, condition.Value, context, negate: true),
            FilterOperator.Contains or FilterOperator.NotContains or FilterOperator.StartsWith => Text(field, member, memberType, condition, context),
            _ => Comparison(field, member, memberType, condition, context),
        };
    }

    private static (Expression Member, Type Type) Member(FieldDef field, ParameterExpression parameter)
    {
        if (field.IsCustom)
        {
            var document = Expression.Property(parameter, nameof(MindLift.Domain.Common.IHasCustomFields.CustomFields));
            var keyExpression = Expression.Constant(field.Key);
            var isNumber = field.Type is FieldType.Number or FieldType.Decimal or FieldType.Currency or FieldType.Percent;
            var method = isNumber
                ? typeof(CustomFieldFunctions).GetMethod(nameof(CustomFieldFunctions.CfNumeric))!
                : typeof(CustomFieldFunctions).GetMethod(nameof(CustomFieldFunctions.CfText))!;
            var call = Expression.Call(method, document, keyExpression);
            return (call, call.Type);
        }

        var property = parameter.Type.GetProperty(field.PropertyName!, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Registry property {field.PropertyName} is missing on {parameter.Type.Name}.");
        var access = Expression.Property(parameter, property);
        return (access, access.Type);
    }

    private static Expression Comparison(FieldDef field, Expression member, Type memberType, FilterCondition condition, FilterContext context)
    {
        var value = Value(field, condition.Value, memberType, context);
        var constant = Expression.Constant(value, memberType);

        return condition.Op switch
        {
            FilterOperator.Eq => Expression.Equal(member, constant),
            FilterOperator.Neq => Expression.NotEqual(member, constant),
            FilterOperator.Gt => Expression.GreaterThan(member, constant),
            FilterOperator.Gte => Expression.GreaterThanOrEqual(member, constant),
            FilterOperator.Lt => Expression.LessThan(member, constant),
            FilterOperator.Lte => Expression.LessThanOrEqual(member, constant),
            _ => throw Invalid(field.Key, $"Operator {condition.Op} is not valid here."),
        };
    }

    private static Expression Text(FieldDef field, Expression member, Type memberType, FilterCondition condition, FilterContext context)
    {
        if (memberType != typeof(string))
        {
            throw Invalid(field.Key, $"'{condition.Op}' only works on text fields.");
        }

        var term = (string)Value(field, condition.Value, typeof(string), context)! ?? string.Empty;

        // ToLower, not ToLowerInvariant: the PostgreSQL provider translates the first and rejects the second.
        // The null test keeps the same expression safe when it runs in memory, and means "does not contain" includes missing values.
        var lowered = Expression.Call(member, nameof(string.ToLower), Type.EmptyTypes);
        var needle = Expression.Constant(term.ToLowerInvariant());
        var hasValue = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));

        return condition.Op switch
        {
            FilterOperator.Contains => Expression.AndAlso(hasValue, Expression.Call(lowered, nameof(string.Contains), Type.EmptyTypes, needle)),
            FilterOperator.NotContains => Expression.Not(Expression.AndAlso(hasValue, Expression.Call(lowered, nameof(string.Contains), Type.EmptyTypes, needle))),
            _ => Expression.AndAlso(hasValue, Expression.Call(lowered, nameof(string.StartsWith), Type.EmptyTypes, needle)),
        };
    }

    private static Expression IsEmpty(Expression member, Type memberType)
    {
        // A value that can never be null (an owner id, a created date, an amount) is never "empty".
        if (memberType.IsValueType && Nullable.GetUnderlyingType(memberType) is null)
        {
            return Expression.Constant(false);
        }

        var isNull = Expression.Equal(member, Expression.Constant(null, memberType));
        if (memberType != typeof(string))
        {
            return isNull;
        }

        var isBlank = Expression.Equal(member, Expression.Constant(string.Empty));
        return Expression.OrElse(isNull, isBlank);
    }

    private static Expression Between(FieldDef field, Expression member, Type memberType, JsonElement? value, FilterContext context)
    {
        if (value is not { ValueKind: JsonValueKind.Object } range)
        {
            throw Invalid(field.Key, "Between needs an object with 'from' and 'to'.");
        }

        var from = Expression.Constant(Value(field, range.GetProperty("from"), memberType, context), memberType);
        var to = Expression.Constant(Value(field, range.GetProperty("to"), memberType, context), memberType);
        return Expression.AndAlso(
            Expression.GreaterThanOrEqual(member, from),
            Expression.LessThanOrEqual(member, to));
    }

    private static Expression Membership(FieldDef field, Expression member, Type memberType, JsonElement? value, FilterContext context, bool negate)
    {
        if (value is not { ValueKind: JsonValueKind.Array } items)
        {
            throw Invalid(field.Key, "In and NotIn need an array of values.");
        }

        var values = items.EnumerateArray().Select(item => Value(field, item, memberType, context)).ToArray();
        var array = Array.CreateInstance(memberType, values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            array.SetValue(values[i], i);
        }

        var arrayType = array.GetType();
        var contains = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(memberType);
        var call = Expression.Call(contains, Expression.Constant(array, arrayType), member);
        return negate ? Expression.Not(call) : call;
    }

    private static Expression DatePresetCondition(FieldDef field, Expression member, JsonElement? value, FilterContext context)
    {
        var (preset, n) = ParsePreset(field, value);
        var range = DateRangeResolver.Resolve(preset, context.NowUtc, context.Zone, n, context.WeekStart, context.FiscalYearStartMonth);

        if (field.IsCustom)
        {
            // Custom dates are stored as yyyy-MM-dd text in the tenant's local calendar.
            var startLocal = TimeZoneInfo.ConvertTimeFromUtc(range.StartUtc, context.Zone).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var endLocal = TimeZoneInfo.ConvertTimeFromUtc(range.EndUtc, context.Zone).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Expression.AndAlso(
                Expression.GreaterThanOrEqual(member, Expression.Constant(startLocal)),
                Expression.LessThan(member, Expression.Constant(endLocal)));
        }

        if (member.Type != typeof(DateTime) && member.Type != typeof(DateTime?))
        {
            throw Invalid(field.Key, "Date presets only work on date fields.");
        }

        var start = Expression.Constant(range.StartUtc, typeof(DateTime?));
        var end = Expression.Constant(range.EndUtc, typeof(DateTime?));
        var nullableMember = Expression.Convert(member, typeof(DateTime?));
        return Expression.AndAlso(
            Expression.GreaterThanOrEqual(nullableMember, start),
            Expression.LessThan(nullableMember, end));
    }

    private static (DatePreset Preset, int N) ParsePreset(FieldDef field, JsonElement? value)
    {
        if (value is { ValueKind: JsonValueKind.String } name && Enum.TryParse<DatePreset>(name.GetString(), out var preset))
        {
            return (preset, 0);
        }

        if (value is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty("preset", out var presetName)
            && Enum.TryParse<DatePreset>(presetName.GetString(), out var named))
        {
            var n = obj.TryGetProperty("n", out var days) ? days.GetInt32() : 0;
            return (named, n);
        }

        throw Invalid(field.Key, "Unknown date preset.");
    }

    /// <summary>Converts a JSON value to the CLR type of the member. "@me" resolves to the current user.</summary>
    private static object? Value(FieldDef field, JsonElement? element, Type target, FilterContext context)
    {
        if (element is null || element.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var json = element.Value;
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        try
        {
            if (underlying == typeof(string))
            {
                var text = json.GetString();
                if (text == "@me" && context.CurrentUserId is { } me)
                {
                    return me.ToString();
                }

                return text;
            }

            if (underlying == typeof(Guid))
            {
                if (json.ValueKind == JsonValueKind.String && json.GetString() == "@me")
                {
                    return context.CurrentUserId ?? throw Invalid(field.Key, "@me needs a signed-in user.");
                }

                return json.GetGuid();
            }

            if (underlying == typeof(decimal))
            {
                return json.ValueKind == JsonValueKind.String
                    ? decimal.Parse(json.GetString()!, CultureInfo.InvariantCulture)
                    : json.GetDecimal();
            }

            if (underlying == typeof(int))
            {
                return json.GetInt32();
            }

            if (underlying == typeof(bool))
            {
                return json.GetBoolean();
            }

            if (underlying == typeof(DateTime))
            {
                var parsed = DateTime.Parse(json.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                return parsed.Kind == DateTimeKind.Local ? parsed.ToUniversalTime() : DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or KeyNotFoundException)
        {
            throw Invalid(field.Key, $"The value for '{field.Key}' is not valid.");
        }

        throw Invalid(field.Key, $"Field '{field.Key}' cannot be compared with this value.");
    }

    private static ValidationException Invalid(string key, string message)
        => new(new Dictionary<string, string[]> { [key] = [message] });
}
