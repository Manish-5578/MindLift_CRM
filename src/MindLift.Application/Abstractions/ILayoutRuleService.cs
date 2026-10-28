using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 2 / #10: Conditional Field Visibility / Layout Rules (layout-rules).
/// Service abstraction for evaluating and managing layout rules.
/// </summary>
public interface ILayoutRuleService
{
    Task<IReadOnlyList<LayoutRuleDto>> ListAsync(string? entityType = null, CancellationToken cancellationToken = default);
    Task<LayoutRuleDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LayoutRuleDto> CreateAsync(SaveLayoutRuleRequest request, CancellationToken cancellationToken = default);
    Task<LayoutRuleDto> UpdateAsync(Guid id, SaveLayoutRuleRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LayoutRuleEvaluationResult> EvaluateAsync(string entityType, IReadOnlyDictionary<string, object?> fieldValues, CancellationToken cancellationToken = default);
    Task<LayoutRuleValidationResult> ValidateAsync(string entityType, IReadOnlyDictionary<string, object?> fieldValues, CancellationToken cancellationToken = default);
}
