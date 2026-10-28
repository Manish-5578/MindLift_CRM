using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 2 / #10: Conditional Field Visibility / Layout Rules (layout-rules).
/// Dynamically shows, hides, requires, or marks fields read-only based on condition evaluation.
/// </summary>
[Audited]
public sealed class LayoutRule : TenantEntity
{
    /// <summary>Entity type (e.g. "leads", "contacts", "accounts", "deals") or empty for all entities.</summary>
    public string EntityType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Evaluation sequence. Lower numbers run first.</summary>
    public int Priority { get; set; }

    /// <summary>Serialized RuleNode JSON condition tree evaluated by RuleEvaluator.</summary>
    public string? ConditionJson { get; set; }

    /// <summary>Fields to make visible when condition is true.</summary>
    public string[] ShowFields { get; set; } = [];

    /// <summary>Fields to hide when condition is true.</summary>
    public string[] HideFields { get; set; } = [];

    /// <summary>Fields to mark mandatory/required when condition is true.</summary>
    public string[] MandatoryFields { get; set; } = [];

    /// <summary>Fields to make read-only when condition is true.</summary>
    public string[] ReadOnlyFields { get; set; } = [];
}
