using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 2 / #11: Custom Object Builder (custom-objects).
/// Service abstraction for defining custom objects and managing their dynamic records.
/// </summary>
public interface ICustomObjectService
{
    Task<IReadOnlyList<CustomObjectDefinitionDto>> ListDefinitionsAsync(bool activeOnly = false, CancellationToken cancellationToken = default);

    Task<CustomObjectDefinitionDto> GetDefinitionAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CustomObjectDefinitionDto> GetDefinitionBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<CustomObjectDefinitionDto> CreateDefinitionAsync(SaveCustomObjectDefinitionRequest request, CancellationToken cancellationToken = default);

    Task<CustomObjectDefinitionDto> UpdateDefinitionAsync(Guid id, SaveCustomObjectDefinitionRequest request, CancellationToken cancellationToken = default);

    Task DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CustomObjectRecordPagedResult> ListRecordsAsync(string slug, CustomObjectRecordQuery query, CancellationToken cancellationToken = default);

    Task<CustomObjectRecordDto> GetRecordAsync(string slug, Guid recordId, CancellationToken cancellationToken = default);

    Task<CustomObjectRecordDto> CreateRecordAsync(string slug, SaveCustomObjectRecordRequest request, CancellationToken cancellationToken = default);

    Task<CustomObjectRecordDto> UpdateRecordAsync(string slug, Guid recordId, SaveCustomObjectRecordRequest request, CancellationToken cancellationToken = default);

    Task DeleteRecordAsync(string slug, Guid recordId, CancellationToken cancellationToken = default);
}
