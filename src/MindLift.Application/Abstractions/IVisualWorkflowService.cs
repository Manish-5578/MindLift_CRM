using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 2 / #12: Visual Workflow Builder (workflow-builder).
/// Service abstraction for visual DAG workflow design, evaluation, and simulation.
/// </summary>
public interface IVisualWorkflowService
{
    Task<IReadOnlyList<VisualWorkflowDto>> ListAsync(string? entityType = null, CancellationToken cancellationToken = default);

    Task<VisualWorkflowDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<VisualWorkflowDto> CreateAsync(SaveVisualWorkflowRequest request, CancellationToken cancellationToken = default);

    Task<VisualWorkflowDto> UpdateAsync(Guid id, SaveVisualWorkflowRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WorkflowSimulationResult> SimulateAsync(Guid id, SimulateWorkflowRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VisualWorkflowExecutionDto>> ListExecutionsAsync(Guid workflowId, int limit = 50, CancellationToken cancellationToken = default);
}
