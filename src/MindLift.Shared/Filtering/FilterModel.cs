using System.Text.Json;

namespace MindLift.Shared.Filtering;

/// <summary>
/// The field types the Field Registry knows about (plan 4.6.1). Used by filters, forms, imports and reports.
/// </summary>
public enum FieldType
{
    Text,
    TextArea,
    Email,
    Phone,
    Url,
    Number,
    Decimal,
    Currency,
    Percent,
    Date,
    DateTime,
    Boolean,
    Picklist,
    MultiPicklist,
    User,
    Lookup,
    Tags,
    File,
}

/// <summary>Operators of the shared filter model (plan 4.6.3).</summary>
public enum FilterOperator
{
    Eq,
    Neq,
    Contains,
    NotContains,
    StartsWith,
    IsEmpty,
    IsNotEmpty,
    Gt,
    Gte,
    Lt,
    Lte,
    Between,
    In,
    NotIn,

    /// <summary>Date presets: the value is a DatePreset name (for example "ThisMonth") or {"n": 30} for LastNDays / NextNDays.</summary>
    DatePreset,
}

/// <summary>A single condition, for example owner = @me.</summary>
public sealed class FilterCondition
{
    /// <summary>Field key from the registry, for example "status_id" or "cf_budget".</summary>
    public string Field { get; set; } = string.Empty;

    public FilterOperator Op { get; set; }

    /// <summary>A scalar, an array, or an object {"from": .., "to": ..} for Between.</summary>
    public JsonElement? Value { get; set; }
}

/// <summary>Conditions combined with AND or OR. Groups may nest up to 3 levels deep.</summary>
public sealed class FilterGroup
{
    public string Logic { get; set; } = "and";
    public List<FilterCondition> Conditions { get; set; } = [];
    public List<FilterGroup> Groups { get; set; } = [];

    public const int MaxDepth = 3;
}
