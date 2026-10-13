using System.Text.Json;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;

namespace MindLift.Shared.Contracts;

/// <summary>
/// Configuration for a specific automation action.
/// </summary>
public sealed record AutomationActionDefinition(
    AutomationActionType Type,
    string? FieldKey = null,
    string? FieldValue = null,
    string? Subject = null,
    int? OffsetDays = null,
    string? Priority = null,
    string? NotificationMessage = null,
    string? WebhookUrl = null,
    string? WebhookMethod = null,
    string? EmailRecipient = null,
    string? EmailBody = null,
    int? ScheduledDelayMinutes = null
);

/// <summary>
/// Trigger criteria for FieldChanged or StageChanged triggers.
/// </summary>
public sealed record AutomationTriggerConfig(
    string? FieldKey = null,
    string? FromValue = null,
    string? ToValue = null
);

/// <summary>
/// Full DTO representing an automation rule.
/// </summary>
public sealed record AutomationRuleDto(
    Guid Id,
    string Name,
    string EntityType,
    AutomationTriggerType TriggerType,
    AutomationTriggerConfig? TriggerConfig,
    FilterGroup? Conditions,
    IReadOnlyList<AutomationActionDefinition> Actions,
    bool IsActive,
    int Priority,
    DateTime CreatedAt
);

/// <summary>
/// Request to create or update an automation rule.
/// </summary>
public sealed record SaveAutomationRuleRequest(
    string Name,
    string EntityType,
    AutomationTriggerType TriggerType,
    AutomationTriggerConfig? TriggerConfig,
    FilterGroup? Conditions,
    IReadOnlyList<AutomationActionDefinition> Actions,
    bool IsActive,
    int Priority
);

/// <summary>
/// Execution history log entry for auditing and diagnostics.
/// </summary>
public sealed record AutomationExecutionLogDto(
    Guid Id,
    Guid RuleId,
    string RuleName,
    string EntityType,
    Guid EntityId,
    string TriggerType,
    string Status,
    string? ErrorMessage,
    DateTime ExecutedAt
);
