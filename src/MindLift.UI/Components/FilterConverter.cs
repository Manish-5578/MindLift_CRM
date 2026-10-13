using System.Globalization;
using System.Text.Json;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;

namespace MindLift.UI.Components;

/// <summary>One line of the filter builder: a field, an operator and whatever value the operator needs.</summary>
public sealed class FilterRow
{
    public string Field { get; set; } = string.Empty;
    public FilterOperator Op { get; set; }

    /// <summary>Text fields, and "@me" or a user id for user fields.</summary>
    public string? Text { get; set; }

    public decimal? Number { get; set; }

    /// <summary>The upper bound of a numeric "is between".</summary>
    public decimal? Number2 { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>A date preset code such as "ThisMonth".</summary>
    public string? Preset { get; set; }

    /// <summary>Chosen values of a picklist, for "is any of" and "is none of".</summary>
    public HashSet<string> Choices { get; set; } = [];

    public bool? Flag { get; set; }
}

/// <summary>
/// Turns builder rows into the shared <see cref="FilterGroup"/> the API understands, and back.
/// A row that is not finished (no value yet) is left out, so a half-filled line never produces an invalid filter.
/// </summary>
public static class FilterConverter
{
    public static FilterCondition? ToCondition(FilterRow row, FieldDto field)
    {
        var value = ValueFor(row, field.Type);
        if (value is null && !FilterOperators.NeedsNoValue(row.Op))
        {
            return null;
        }

        return new FilterCondition { Field = row.Field, Op = row.Op, Value = value };
    }

    public static FilterGroup ToGroup(IEnumerable<FilterRow> rows, IReadOnlyList<FieldDto> fields, string logic = "and", IEnumerable<FilterGroup>? keepGroups = null)
    {
        var group = new FilterGroup { Logic = logic };
        foreach (var row in rows)
        {
            var field = fields.FirstOrDefault(f => f.Key == row.Field);
            if (field is null)
            {
                continue;
            }

            if (ToCondition(row, field) is { } condition)
            {
                group.Conditions.Add(condition);
            }
        }

        // Nested groups cannot be edited here yet. Keep them as they were so saved views are not changed by opening them.
        group.Groups.AddRange(keepGroups ?? []);
        return group;
    }

    public static FilterRow ToRow(FilterCondition condition, FieldDto field)
    {
        var row = new FilterRow { Field = condition.Field, Op = condition.Op };
        if (condition.Value is not { } value)
        {
            return row;
        }

        var isDate = field.Type is FieldType.Date or FieldType.DateTime;
        switch (condition.Op)
        {
            case FilterOperator.DatePreset:
                row.Preset = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                break;
            case FilterOperator.Between when value.ValueKind == JsonValueKind.Object:
                if (isDate)
                {
                    row.From = ParseDate(value, "from");
                    row.To = ParseDate(value, "to");
                }
                else
                {
                    row.Number = value.TryGetProperty("from", out var from) && from.TryGetDecimal(out var f) ? f : null;
                    row.Number2 = value.TryGetProperty("to", out var to) && to.TryGetDecimal(out var t) ? t : null;
                }

                break;
            case FilterOperator.In or FilterOperator.NotIn when value.ValueKind == JsonValueKind.Array:
                row.Choices = value.EnumerateArray().Select(e => e.ToString()).ToHashSet();
                break;
            default:
                switch (value.ValueKind)
                {
                    case JsonValueKind.True or JsonValueKind.False:
                        row.Flag = value.GetBoolean();
                        break;
                    case JsonValueKind.Number:
                        row.Number = value.GetDecimal();
                        break;
                    case JsonValueKind.String when isDate:
                        row.From = DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d.Date : null;
                        break;
                    case JsonValueKind.String:
                        row.Text = value.GetString();
                        break;
                }

                break;
        }

        return row;
    }

    private static JsonElement? ValueFor(FilterRow row, FieldType type)
    {
        var isDate = type is FieldType.Date or FieldType.DateTime;
        var isNumber = type is FieldType.Number or FieldType.Decimal or FieldType.Currency or FieldType.Percent;

        switch (row.Op)
        {
            case FilterOperator.IsEmpty or FilterOperator.IsNotEmpty:
                return null;
            case FilterOperator.DatePreset:
                return string.IsNullOrWhiteSpace(row.Preset) ? null : JsonSerializer.SerializeToElement(row.Preset);
            case FilterOperator.Between when isDate:
                return row.From is { } from && row.To is { } to
                    ? JsonSerializer.SerializeToElement(new { from = Iso(from), to = Iso(to) })
                    : null;
            case FilterOperator.Between:
                return row.Number is { } low && row.Number2 is { } high
                    ? JsonSerializer.SerializeToElement(new { from = low, to = high })
                    : null;
            case FilterOperator.In or FilterOperator.NotIn:
                return row.Choices.Count == 0 ? null : JsonSerializer.SerializeToElement(row.Choices.OrderBy(c => c, StringComparer.Ordinal).ToArray());
        }

        if (type == FieldType.Boolean)
        {
            return row.Flag is { } flag ? JsonSerializer.SerializeToElement(flag) : null;
        }

        if (isDate)
        {
            return row.From is { } date ? JsonSerializer.SerializeToElement(Iso(date)) : null;
        }

        if (isNumber)
        {
            return row.Number is { } number ? JsonSerializer.SerializeToElement(number) : null;
        }

        return string.IsNullOrWhiteSpace(row.Text) ? null : JsonSerializer.SerializeToElement(row.Text.Trim());
    }

    private static string Iso(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime? ParseDate(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var p) && DateTime.TryParse(p.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d.Date : null;
}
