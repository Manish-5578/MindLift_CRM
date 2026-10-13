namespace MindLift.Shared.Filtering;

/// <summary>
/// Which operators the filter builder offers for each field type, and what they are called (plan 4.6.3 and 18.8.1).
/// A test compiles every combination against the real field registry, so the screen never offers a filter the server cannot run.
/// </summary>
public static class FilterOperators
{
    private static readonly FilterOperator[] Text =
        [FilterOperator.Contains, FilterOperator.NotContains, FilterOperator.StartsWith, FilterOperator.Eq, FilterOperator.Neq, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty];

    private static readonly FilterOperator[] Numeric =
        [FilterOperator.Eq, FilterOperator.Neq, FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte, FilterOperator.Between, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty];

    private static readonly FilterOperator[] Dates =
        [FilterOperator.DatePreset, FilterOperator.Gte, FilterOperator.Lte, FilterOperator.Between, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty];

    private static readonly FilterOperator[] Choice =
        [FilterOperator.In, FilterOperator.NotIn, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty];

    private static readonly FilterOperator[] Person =
        [FilterOperator.Eq, FilterOperator.Neq, FilterOperator.IsEmpty, FilterOperator.IsNotEmpty];

    private static readonly FilterOperator[] Flag = [FilterOperator.Eq];

    private static readonly FilterOperator[] Presence = [FilterOperator.IsEmpty, FilterOperator.IsNotEmpty];

    /// <summary>The operators for a field type, in the order the drop-down shows them. The first one is the default.</summary>
    public static IReadOnlyList<FilterOperator> For(FieldType type) => type switch
    {
        FieldType.Text or FieldType.TextArea or FieldType.Email or FieldType.Phone or FieldType.Url => Text,
        FieldType.Number or FieldType.Decimal or FieldType.Currency or FieldType.Percent => Numeric,
        FieldType.Date or FieldType.DateTime => Dates,
        FieldType.Boolean => Flag,
        FieldType.Picklist => Choice,
        FieldType.User or FieldType.Lookup => Person,
        FieldType.MultiPicklist or FieldType.Tags or FieldType.File => Presence,
        _ => Presence,
    };

    /// <summary>True when the operator needs no value from the user (is empty, is not empty).</summary>
    public static bool NeedsNoValue(FilterOperator op) => op is FilterOperator.IsEmpty or FilterOperator.IsNotEmpty;

    public static string Label(FilterOperator op) => op switch
    {
        FilterOperator.Eq => "is",
        FilterOperator.Neq => "is not",
        FilterOperator.Contains => "contains",
        FilterOperator.NotContains => "does not contain",
        FilterOperator.StartsWith => "starts with",
        FilterOperator.IsEmpty => "is empty",
        FilterOperator.IsNotEmpty => "is not empty",
        FilterOperator.Gt => "is greater than",
        FilterOperator.Gte => "is at least",
        FilterOperator.Lt => "is less than",
        FilterOperator.Lte => "is at most",
        FilterOperator.Between => "is between",
        FilterOperator.In => "is any of",
        FilterOperator.NotIn => "is none of",
        FilterOperator.DatePreset => "is in",
        _ => op.ToString(),
    };

    /// <summary>The labels for the date presets in the order users expect, with the code the API expects.</summary>
    public static IReadOnlyList<(string Code, string Label)> DatePresets { get; } =
    [
        ("Today", "Today"),
        ("Yesterday", "Yesterday"),
        ("ThisWeek", "This week"),
        ("LastWeek", "Last week"),
        ("ThisMonth", "This month"),
        ("LastMonth", "Last month"),
        ("ThisQuarter", "This quarter"),
        ("LastQuarter", "Last quarter"),
        ("ThisYear", "This year"),
        ("LastYear", "Last year"),
    ];
}
