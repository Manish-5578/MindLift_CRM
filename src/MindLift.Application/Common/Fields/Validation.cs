using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;

namespace MindLift.Application.Common.Fields;

/// <summary>Rules for custom field definitions and for values stored in custom fields (plan 4.6.2).</summary>
public static partial class CustomFieldRules
{
    public const int MaxFieldsPerEntity = 150;

    [GeneratedRegex("^cf_[a-z][a-z0-9_]{1,46}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    public static bool IsValidKey(string key) => KeyPattern().IsMatch(key);

    /// <summary>Validates a value map (custom keys only) against the definitions. Returns field errors, empty when valid.</summary>
    public static Dictionary<string, string[]> ValidateValues(
        IReadOnlyList<CustomFieldRule> definitions,
        IReadOnlyDictionary<string, JsonElement?> values)
    {
        var errors = new Dictionary<string, List<string>>();

        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
            {
                errors[key] = list = [];
            }

            list.Add(message);
        }

        foreach (var key in values.Keys)
        {
            if (definitions.All(d => d.Key != key))
            {
                Add(key, "Unknown custom field.");
            }
        }

        foreach (var definition in definitions)
        {
            values.TryGetValue(definition.Key, out var value);
            var missing = value is null || value.Value.ValueKind == JsonValueKind.Null
                || (value.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.Value.GetString()));

            if (missing)
            {
                if (definition.IsRequired)
                {
                    Add(definition.Key, $"{definition.Label} is required.");
                }

                continue;
            }

            var message = CheckType(definition, value!.Value);
            if (message is not null)
            {
                Add(definition.Key, message);
            }
        }

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    private static string? CheckType(CustomFieldRule definition, JsonElement value)
    {
        switch (definition.Type)
        {
            case FieldType.Number:
            case FieldType.Decimal:
            case FieldType.Currency:
            case FieldType.Percent:
                return value.ValueKind == JsonValueKind.Number ? null : $"{definition.Label} must be a number.";

            case FieldType.Boolean:
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : $"{definition.Label} must be true or false.";

            case FieldType.Date:
                return value.ValueKind == JsonValueKind.String
                       && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? null
                    : $"{definition.Label} must be a date (yyyy-MM-dd).";

            case FieldType.DateTime:
                return value.ValueKind == JsonValueKind.String
                       && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? null
                    : $"{definition.Label} must be a date and time.";

            case FieldType.Picklist:
                return value.ValueKind == JsonValueKind.String && definition.OptionValues.Contains(value.GetString()!)
                    ? null
                    : $"{definition.Label} must be one of the listed options.";

            case FieldType.MultiPicklist:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    return $"{definition.Label} must be a list of options.";
                }

                return value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String && definition.OptionValues.Contains(v.GetString()!))
                    ? null
                    : $"{definition.Label} contains an option that does not exist.";

            case FieldType.Text:
            case FieldType.TextArea:
            case FieldType.Email:
            case FieldType.Phone:
            case FieldType.Url:
                if (value.ValueKind != JsonValueKind.String)
                {
                    return $"{definition.Label} must be text.";
                }

                return value.GetString()!.Length > 2000 ? $"{definition.Label} is too long (2000 characters at most)." : null;

            default:
                return null;
        }
    }
}

/// <summary>What the validator needs from a definition, without depending on the database entity.</summary>
public sealed record CustomFieldRule(string Key, string Label, FieldType Type, bool IsRequired, IReadOnlyCollection<string> OptionValues);

/// <summary>Rules for the stages of one pipeline (plan 7.7 and 5.3).</summary>
public static class PipelineRules
{
    public static readonly string[] StageTypes = ["Open", "Won", "Lost"];

    /// <summary>
    /// Checks a full, ordered list of stages: at least one Open stage, exactly one Won and one Lost,
    /// Won and Lost come after every Open stage, and probabilities are 0 to 100.
    /// Returns an error message, or null when the list is valid.
    /// </summary>
    public static string? Validate(IReadOnlyList<(string StageType, int Probability)> orderedStages)
    {
        if (orderedStages.Any(s => !StageTypes.Contains(s.StageType)))
        {
            return "Each stage must be Open, Won or Lost.";
        }

        if (orderedStages.Any(s => s.Probability is < 0 or > 100))
        {
            return "Probability must be between 0 and 100.";
        }

        if (orderedStages.All(s => s.StageType != "Open"))
        {
            return "A pipeline needs at least one open stage.";
        }

        if (orderedStages.Count(s => s.StageType == "Won") != 1 || orderedStages.Count(s => s.StageType == "Lost") != 1)
        {
            return "A pipeline needs exactly one Won stage and exactly one Lost stage.";
        }

        var lastOpen = orderedStages.Select((s, i) => (s, i)).Where(x => x.s.StageType == "Open").Max(x => x.i);
        var firstClosed = orderedStages.Select((s, i) => (s, i)).Where(x => x.s.StageType != "Open").Min(x => x.i);
        if (firstClosed < lastOpen)
        {
            return "Won and Lost must be the last stages of the pipeline.";
        }

        return null;
    }
}
