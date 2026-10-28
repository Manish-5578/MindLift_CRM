using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 2 / #9: Dependent Picklists (dependent-picklists).
/// Service abstraction for managing and evaluating picklist dependencies.
/// </summary>
public interface IDependentPicklistService
{
    Task<IReadOnlyList<DependentPicklistMapDto>> ListAsync(string? entityType = null, CancellationToken cancellationToken = default);
    Task<DependentPicklistMapDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DependentPicklistMapDto> CreateAsync(SaveDependentPicklistMapRequest request, CancellationToken cancellationToken = default);
    Task<DependentPicklistMapDto> UpdateAsync(Guid id, SaveDependentPicklistMapRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AllowedPicklistValuesDto> GetAllowedValuesAsync(string entityType, string parentField, string childField, string? parentValue, CancellationToken cancellationToken = default);
    Task<DependentPicklistValidationResult> ValidateAsync(string entityType, IReadOnlyDictionary<string, string?> fieldValues, CancellationToken cancellationToken = default);
}
