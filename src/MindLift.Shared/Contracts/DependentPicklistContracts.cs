namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 2 / #9: Dependent Picklists (dependent-picklists).
/// Contracts for defining, querying, evaluating, and validating parent/child picklist dependencies.
/// </summary>
public sealed record DependentPicklistMapDto(
    Guid Id,
    string EntityType,
    string ParentField,
    string ChildField,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Mappings);

public sealed record SaveDependentPicklistMapRequest(
    string EntityType,
    string ParentField,
    string ChildField,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Mappings);

public sealed record GetAllowedPicklistValuesRequest(
    string EntityType,
    string ParentField,
    string ChildField,
    string? ParentValue);

public sealed record AllowedPicklistValuesDto(
    string ChildField,
    string? ParentValue,
    IReadOnlyList<string> AllowedValues,
    IReadOnlyList<PicklistValueDto> AllowedOptions);

public sealed record ValidateDependentPicklistRequest(
    string EntityType,
    IReadOnlyDictionary<string, string?> FieldValues);

public sealed record DependentPicklistValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);
