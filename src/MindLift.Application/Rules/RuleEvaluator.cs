using System.Globalization;
using MindLift.Shared.Contracts;

namespace MindLift.Application.Rules;

/// <summary>
/// Checks and runs a <see cref="RuleNode"/> tree against the facts of one record (field name to value).
/// It is a pure calculation: no database, no network, no regular expressions (so a rule cannot be written to hang the server),
/// and the size of a rule is capped. A rule that cannot be understood never matches, so a broken rule fails closed.
/// </summary>
public static class RuleEvaluator
{
    public const int MaxDepth = 5;
    public const int MaxNodes = 50;
    public const int MaxValueLength = 500;
    public const int MaxListValues = 50;

    /// <summary>Returns the reasons the rule cannot be saved; an empty list means it is valid. <paramref name="allowedFields"/> limits which fields a condition may read.</summary>
    public static IReadOnlyList<string> Validate(RuleNode? rule, IReadOnlyCollection<string>? allowedFields = null)
    {
        var errors = new List<string>();
        if (rule is null)
        {
            errors.Add("A rule is required.");
            return errors;
        }

        var nodes = 0;
        Check(rule, 1, allowedFields, errors, ref nodes);
        if (nodes > MaxNodes)
        {
            errors.Add($"A rule can have at most {MaxNodes} parts.");
        }

        return errors;
    }

    private static void Check(RuleNode node, int depth, IReadOnlyCollection<string>? allowedFields, List<string> errors, ref int nodes)
    {
        nodes++;
        if (nodes > MaxNodes)
        {
            return;
        }

        if (depth > MaxDepth)
        {
            errors.Add($"Groups can be nested at most {MaxDepth} levels deep.");
            return;
        }

        switch (node.Kind)
        {
            case RuleNode.Group:
                if (node.Combinator is not (RuleNode.And or RuleNode.Or))
                {
                    errors.Add("A group must combine its parts with 'and' or 'or'.");
                }

                if (node.Children is null || node.Children.Count == 0)
                {
                    errors.Add("A group needs at least one part.");
                    return;
                }

                foreach (var child in node.Children)
                {
                    if (child is null)
                    {
                        errors.Add("A group cannot contain an empty part.");
                        continue;
                    }

                    Check(child, depth + 1, allowedFields, errors, ref nodes);
                    if (nodes > MaxNodes)
                    {
                        return;
                    }
                }

                break;

            case RuleNode.Condition:
                CheckCondition(node, allowedFields, errors);
                break;

            default:
                errors.Add("Each part of a rule must be a group or a condition.");
                break;
        }
    }

    private static void CheckCondition(RuleNode node, IReadOnlyCollection<string>? allowedFields, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(node.Field))
        {
            errors.Add("Choose a field for every condition.");
        }
        else if (allowedFields is not null && !allowedFields.Contains(node.Field, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"'{node.Field}' is not a field this rule can use.");
        }

        if (node.Operator is null || !RuleOperators.All.Contains(node.Operator))
        {
            errors.Add($"'{node.Operator}' is not a known comparison.");
            return;
        }

        if (RuleOperators.NoValue.Contains(node.Operator))
        {
            return;
        }

        if (RuleOperators.ListValue.Contains(node.Operator))
        {
            if (node.Values is null || node.Values.Count == 0)
            {
                errors.Add("Give at least one value to compare with.");
            }
            else if (node.Values.Count > MaxListValues || node.Values.Any(v => v is null || v.Length > MaxValueLength))
            {
                errors.Add($"A list can have at most {MaxListValues} values of {MaxValueLength} characters.");
            }

            return;
        }

        if (node.Value is null)
        {
            errors.Add("Give a value to compare with.");
        }
        else if (node.Value.Length > MaxValueLength)
        {
            errors.Add($"A value can have at most {MaxValueLength} characters.");
        }
    }

    /// <summary>True when the record matches the rule. An invalid or too large rule never matches.</summary>
    public static bool Evaluate(RuleNode? rule, IReadOnlyDictionary<string, object?> facts)
    {
        if (rule is null || Validate(rule).Count > 0)
        {
            return false;
        }

        // Field names are matched without regard to upper and lower case.
        var folded = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in facts)
        {
            folded[pair.Key] = pair.Value;
        }

        return Run(rule, folded);
    }

    private static bool Run(RuleNode node, Dictionary<string, object?> facts)
    {
        if (node.Kind == RuleNode.Group)
        {
            var children = node.Children!;
            return node.Combinator == RuleNode.And
                ? children.All(c => Run(c, facts))
                : children.Any(c => Run(c, facts));
        }

        facts.TryGetValue(node.Field!, out var raw);
        return Compare(Text(raw), node.Operator!, node.Value, node.Values);
    }

    private static bool Compare(string? actual, string op, string? expected, IReadOnlyList<string>? list)
    {
        switch (op)
        {
            case RuleOperators.IsEmpty:
                return string.IsNullOrWhiteSpace(actual);
            case RuleOperators.IsNotEmpty:
                return !string.IsNullOrWhiteSpace(actual);
            case RuleOperators.In:
                return actual is not null && list!.Any(v => Same(actual, v));
            case RuleOperators.NotIn:
                return actual is null || !list!.Any(v => Same(actual, v));
            case RuleOperators.Contains:
                return actual is not null && expected is not null && actual.Contains(expected, StringComparison.OrdinalIgnoreCase);
            case RuleOperators.NotContains:
                return actual is null || expected is null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase);
            case RuleOperators.StartsWith:
                return actual is not null && expected is not null && actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
            case RuleOperators.EndsWith:
                return actual is not null && expected is not null && actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase);
            case RuleOperators.Eq:
                return actual is not null && expected is not null && Same(actual, expected);
            case RuleOperators.Neq:
                return actual is null || expected is null || !Same(actual, expected);
        }

        if (actual is null || expected is null)
        {
            return false;
        }

        var order = Order(actual, expected);
        return op switch
        {
            RuleOperators.GreaterThan => order > 0,
            RuleOperators.GreaterOrEqual => order >= 0,
            RuleOperators.LessThan => order < 0,
            RuleOperators.LessOrEqual => order <= 0,
            _ => false,
        };
    }

    private static bool Same(string a, string b) => Order(a, b) == 0;

    /// <summary>Numbers compare as numbers and dates as dates; everything else compares as text, ignoring upper and lower case.</summary>
    private static int Order(string a, string b)
    {
        if (decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) && decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out var y))
        {
            return x.CompareTo(y);
        }

        if (DateTimeOffset.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d1) && DateTimeOffset.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d2))
        {
            return d1.CompareTo(d2);
        }

        return string.Compare(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}

