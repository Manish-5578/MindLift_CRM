namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 2 / #10: Conditional Field Visibility / Layout Rules (layout-rules).
/// Contracts for layout rules that conditionally toggle visibility, required status, and read-only attributes.
/// </summary>
public sealed record LayoutRuleDto(
    Guid Id,
    string EntityType,
    string Name,
    string? Description,
    bool IsActive,
    int Priority,
    RuleNode? Condition,
    IReadOnlyList<string> ShowFields,
    IReadOnlyList<string> HideFields,
    IReadOnlyList<string> MandatoryFields,
    IReadOnlyList<string> ReadOnlyFields);

public sealed record SaveLayoutRuleRequest(
    string EntityType,
    string Name,
    string? Description,
    bool IsActive,
    int Priority,
    RuleNode? Condition,
    IReadOnlyList<string>? ShowFields = null,
    IReadOnlyList<string>? HideFields = null,
    IReadOnlyList<string>? MandatoryFields = null,
    IReadOnlyList<string>? ReadOnlyFields = null);

public sealed record EvaluateLayoutRulesRequest(
    string EntityType,
    IReadOnlyDictionary<string, object?> FieldValues);

public sealed record LayoutRuleEvaluationResult(
    string EntityType,
    IReadOnlyList<string> AppliedRules,
    IReadOnlyList<string> VisibleFields,
    IReadOnlyList<string> HiddenFields,
    IReadOnlyList<string> MandatoryFields,
    IReadOnlyList<string> ReadOnlyFields);

public sealed record ValidateLayoutRulesRequest(
    string EntityType,
    IReadOnlyDictionary<string, object?> FieldValues);

public sealed record LayoutRuleValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);
