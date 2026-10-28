using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 2 / #12: Visual Workflow Builder (workflow-builder).
/// Visual Directed Acyclic Graph (DAG) workflow for complex multi-step automation.
/// </summary>
[Audited]
public sealed class VisualWorkflow : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Target entity (e.g. "leads", "contacts", "accounts", "deals", "tickets").</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Trigger category: "record_created", "record_updated", "stage_changed", "manual".</summary>
    public string TriggerType { get; set; } = "record_created";

    public string? TriggerConfigJson { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Serialized JSON array of WorkflowNodeDto nodes.</summary>
    public string NodesJson { get; set; } = "[]";

    /// <summary>Serialized JSON array of WorkflowEdgeDto directed edges.</summary>
    public string EdgesJson { get; set; } = "[]";
}

/// <summary>
/// Phase 2 / #12: Visual Workflow Builder (workflow-builder).
/// Execution history log for visual workflow runs and simulations.
/// </summary>
[Audited]
public sealed class VisualWorkflowExecution : TenantEntity
{
    public Guid WorkflowId { get; set; }

    public string WorkflowName { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    /// <summary>"Success", "Failed", or "Simulated".</summary>
    public string Status { get; set; } = "Success";

    public DateTime ExecutedAt { get; set; }

    public long DurationMs { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Serialized JSON array of WorkflowStepExecutionDto detailing each node's evaluation.</summary>
    public string ExecutionLogJson { get; set; } = "[]";
}
