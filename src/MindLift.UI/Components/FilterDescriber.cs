using System.Globalization;
using System.Text.Json;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;

namespace MindLift.UI.Components;

/// <summary>Turns a filter condition into the short text shown on a removable chip, for example "Status is any of New, Working".</summary>
public static class FilterDescriber
{
    public static string Describe(FilterCondition condition, FieldDto field, IReadOnlyList<PicklistDto>? picklists = null, IReadOnlyList<UserListItem>? users = null)
    {
        var text = $"{field.Label} {FilterOperators.Label(condition.Op)}";
        if (FilterOperators.NeedsNoValue(condition.Op) || condition.Value is not { } value)
        {
            return text;
        }

        return $"{text} {ValueText(condition.Op, value, field, picklists, users)}";
    }

    private static string ValueText(FilterOperator op, JsonElement value, FieldDto field, IReadOnlyList<PicklistDto>? picklists, IReadOnlyList<UserListItem>? users)
    {
        if (op == FilterOperator.DatePreset)
        {
            var code = value.GetString();
            return FilterOperators.DatePresets.FirstOrDefault(p => p.Code == code).Label ?? code ?? string.Empty;
        }

        if (op == FilterOperator.Between && value.ValueKind == JsonValueKind.Object)
        {
            var from = value.TryGetProperty("from", out var f) ? Scalar(f, field, picklists, users) : string.Empty;
            var to = value.TryGetProperty("to", out var t) ? Scalar(t, field, picklists, users) : string.Empty;
            return $"{from} and {to}";
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return string.Join(", ", value.EnumerateArray().Select(e => Scalar(e, field, picklists, users)));
        }

        return Scalar(value, field, picklists, users);
    }

    private static string Scalar(JsonElement value, FieldDto field, IReadOnlyList<PicklistDto>? picklists, IReadOnlyList<UserListItem>? users)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return "Yes";
            case JsonValueKind.False:
                return "No";
            case JsonValueKind.Number:
                return value.GetDecimal().ToString("0.##", CultureInfo.InvariantCulture);
        }

        var raw = value.ToString();
        if (raw == "@me")
        {
            return "Me";
        }

        if (field.Type is FieldType.User && Guid.TryParse(raw, out var id))
        {
            return users?.FirstOrDefault(u => u.Id == id)?.FullName ?? "Someone";
        }

        if (field.Type is FieldType.Date or FieldType.DateTime && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        {
            return date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }

        if (field.Options is { Count: > 0 } && field.Options.FirstOrDefault(o => o.Value == raw) is { } option)
        {
            return option.Label;
        }

        var list = picklists?.FirstOrDefault(p => p.Key == field.PicklistKey);
        return list?.Values.FirstOrDefault(v => v.Value == raw)?.Label ?? raw;
    }
}
