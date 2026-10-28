namespace MindLift.Shared.Contracts;

/// <summary>
/// One node of a rule: either a group that combines its children with AND / OR, or a single condition
/// (a field, an operator and a value). Rules are stored as data, so they are checked before they are saved.
/// </summary>
public sealed record RuleNode(
    string Kind,
    string? Combinator = null,
    IReadOnlyList<RuleNode>? Children = null,
    string? Field = null,
    string? Operator = null,
    string? Value = null,
    IReadOnlyList<string>? Values = null)
{
    public const string Group = "group";
    public const string Condition = "condition";
    public const string And = "and";
    public const string Or = "or";

    public static RuleNode All(params RuleNode[] children) => new(Group, And, children);

    public static RuleNode Any(params RuleNode[] children) => new(Group, Or, children);

    public static RuleNode When(string field, string op, string? value = null) => new(Condition, Field: field, Operator: op, Value: value);

    public static RuleNode WhenIn(string field, string op, params string[] values) => new(Condition, Field: field, Operator: op, Values: values);
}

/// <summary>The comparisons a condition can make. Fixed on purpose: there is no free-form expression or pattern language to abuse.</summary>
public static class RuleOperators
{
    public const string Eq = "eq";
    public const string Neq = "neq";
    public const string GreaterThan = "gt";
    public const string GreaterOrEqual = "gte";
    public const string LessThan = "lt";
    public const string LessOrEqual = "lte";
    public const string Contains = "contains";
    public const string NotContains = "notContains";
    public const string StartsWith = "startsWith";
    public const string EndsWith = "endsWith";
    public const string In = "in";
    public const string NotIn = "notIn";
    public const string IsEmpty = "isEmpty";
    public const string IsNotEmpty = "isNotEmpty";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Eq, Neq, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, Contains, NotContains,
        StartsWith, EndsWith, In, NotIn, IsEmpty, IsNotEmpty,
    };

    /// <summary>Operators that take no value.</summary>
    public static readonly IReadOnlySet<string> NoValue = new HashSet<string>(StringComparer.Ordinal) { IsEmpty, IsNotEmpty };

    /// <summary>Operators that take a list of values.</summary>
    public static readonly IReadOnlySet<string> ListValue = new HashSet<string>(StringComparer.Ordinal) { In, NotIn };
}

