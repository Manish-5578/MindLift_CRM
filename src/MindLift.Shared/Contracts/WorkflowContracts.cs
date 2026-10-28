namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 2 / #12: Visual Workflow Builder (workflow-builder).
/// Contracts for DAG visual workflows, branching condition nodes, actions, and simulation execution.
/// </summary>
public sealed record WorkflowNodeDto(
    string Id,
    string Type,
    string Label,
    double X = 0,
    double Y = 0,
    IReadOnlyDictionary<string, object?>? Config = null,
    RuleNode? Condition = null);

public sealed record WorkflowEdgeDto(
    string Id,
    string SourceNodeId,
    string TargetNodeId,
    string? SourcePort = null,
    string? Label = null);

public sealed record VisualWorkflowDto(
    Guid Id,
    string Name,
    string? Description,
    string EntityType,
    string TriggerType,
    string? TriggerConfigJson,
    bool IsActive,
    IReadOnlyList<WorkflowNodeDto> Nodes,
    IReadOnlyList<WorkflowEdgeDto> Edges,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record SaveVisualWorkflowRequest(
    string Name,
    string? Description,
    string EntityType,
    string TriggerType,
    string? TriggerConfigJson = null,
    bool IsActive = true,
    IReadOnlyList<WorkflowNodeDto>? Nodes = null,
    IReadOnlyList<WorkflowEdgeDto>? Edges = null);

public sealed record WorkflowStepExecutionDto(
    string NodeId,
    string NodeType,
    string NodeLabel,
    string Status,
    string? Output,
    string? Error,
    DateTime ExecutedAt);

public sealed record SimulateWorkflowRequest(
    IReadOnlyDictionary<string, object?> Facts,
    Guid? EntityId = null,
    bool RecordExecution = false);

public sealed record WorkflowSimulationResult(
    bool Success,
    string Status,
    IReadOnlyList<WorkflowStepExecutionDto> Steps,
    IReadOnlyList<string> ExecutedNodeIds,
    IReadOnlyDictionary<string, object?> FinalFacts,
    string? ErrorMessage = null);

public sealed record VisualWorkflowExecutionDto(
    Guid Id,
    Guid WorkflowId,
    string WorkflowName,
    string EntityType,
    Guid? EntityId,
    string Status,
    DateTime ExecutedAt,
    long DurationMs,
    string? ErrorMessage,
    IReadOnlyList<WorkflowStepExecutionDto> Steps);
