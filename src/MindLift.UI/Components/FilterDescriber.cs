using System.Globalization;
using System.Text.Json;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using MindLift.UI.Localization;

namespace MindLift.UI.Components;

/// <summary>
/// Turns a filter condition into the short text shown on a removable chip, for example "Status is any of New, Working".
/// With a translator the words are shown in the current language; without one they stay English.
/// </summary>
public static class FilterDescriber
{
    public static string Describe(FilterCondition condition, FieldDto field, IReadOnlyList<PicklistDto>? picklists = null, IReadOnlyList<UserListItem>? users = null, Translator? translator = null)
    {
        var text = $"{Say(translator, field.Label)} {Say(translator, FilterOperators.Label(condition.Op))}";
        if (FilterOperators.NeedsNoValue(condition.Op) || condition.Value is not { } value)
        {
            return text;
        }

        return $"{text} {ValueText(condition.Op, value, field, picklists, users, translator)}";
    }

    private static string Say(Translator? translator, string english) => translator is null ? english : translator[english];

    private static string ValueText(FilterOperator op, JsonElement value, FieldDto field, IReadOnlyList<PicklistDto>? picklists, IReadOnlyList<UserListItem>? users, Translator? translator)
    {
        if (op == FilterOperator.DatePreset)
        {
            var code = value.GetString();
            var label = FilterOperators.DatePresets.FirstOrDefault(p => p.Code == code).Label;
            return label is null ? code ?? string.Empty : Say(translator, label);
        }

        if (op == FilterOperator.Between && value.ValueKind == JsonValueKind.Object)
        {
            var from = value.TryGetProperty("from", out var f) ? Scalar(f, field, picklists, users, translator) : string.Empty;
            var to = value.TryGetProperty("to", out var t) ? Scalar(t, field, picklists, users, translator) : string.Empty;
            return $"{from} {Say(translator, "and")} {to}";
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return string.Join(", ", value.EnumerateArray().Select(e => Scalar(e, field, picklists, users, translator)));
        }

        return Scalar(value, field, picklists, users, translator);
    }

    private static string Scalar(JsonElement value, FieldDto field, IReadOnlyList<PicklistDto>? picklists, IReadOnlyList<UserListItem>? users, Translator? translator)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return Say(translator, "Yes");
            case JsonValueKind.False:
                return Say(translator, "No");
            case JsonValueKind.Number:
                return value.GetDecimal().ToString("0.##", CultureInfo.InvariantCulture);
        }

        var raw = value.ToString();
        if (raw == "@me")
        {
            return Say(translator, "Me");
        }

        if (field.Type is FieldType.User && Guid.TryParse(raw, out var id))
        {
            return users?.FirstOrDefault(u => u.Id == id)?.FullName ?? Say(translator, "Someone");
        }

        if (field.Type is FieldType.Date or FieldType.DateTime && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        {
            return date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }

        if (field.Options is { Count: > 0 } && field.Options.FirstOrDefault(o => o.Value == raw) is { } option)
        {
            return Say(translator, option.Label);
        }

        var list = picklists?.FirstOrDefault(p => p.Key == field.PicklistKey);
        return list?.Values.FirstOrDefault(v => v.Value == raw)?.Label is { } label ? Say(translator, label) : raw;
    }
}
