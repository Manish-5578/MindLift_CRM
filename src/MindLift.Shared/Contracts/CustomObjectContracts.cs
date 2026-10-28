namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 2 / #11: Custom Object Builder (custom-objects).
/// Contracts for user-defined business objects, custom schemas, and dynamic record payloads.
/// </summary>
public sealed record CustomObjectFieldDto(
    string Key,
    string Label,
    string? LabelAr = null,
    string Type = "text",
    bool IsRequired = false,
    string? DefaultValue = null,
    IReadOnlyList<string>? Options = null,
    string? LookupEntityType = null);

public sealed record CustomObjectDefinitionDto(
    Guid Id,
    string Name,
    string PluralName,
    string Slug,
    string? Description,
    string? Icon,
    string TitleField,
    bool IsActive,
    IReadOnlyList<CustomObjectFieldDto> Fields,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record SaveCustomObjectDefinitionRequest(
    string Name,
    string PluralName,
    string Slug,
    string? Description = null,
    string? Icon = null,
    string? TitleField = null,
    bool IsActive = true,
    IReadOnlyList<CustomObjectFieldDto>? Fields = null);

public sealed record CustomObjectRecordDto(
    Guid Id,
    Guid DefinitionId,
    string DefinitionSlug,
    int RecordNumber,
    string DisplayName,
    IReadOnlyDictionary<string, object?> Data,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? CreatedById = null,
    Guid? UpdatedById = null);

public sealed record SaveCustomObjectRecordRequest(
    IReadOnlyDictionary<string, object?> Data);

public sealed record CustomObjectRecordPagedResult(
    IReadOnlyList<CustomObjectRecordDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record CustomObjectRecordQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null);
