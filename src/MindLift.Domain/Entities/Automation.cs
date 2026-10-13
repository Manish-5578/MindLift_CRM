using System.Text.Json;
using MindLift.Domain.Common;
using MindLift.Shared.Enums;

namespace MindLift.Domain.Entities;

/// <summary>
/// A Trigger-Condition-Action automation rule (Zoho CRM Automation Engine).
/// </summary>
[Audited]
public sealed class AutomationRule : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>leads, contacts, accounts, deals, activities.</summary>
    public string EntityType { get; set; } = string.Empty;

    public AutomationTriggerType TriggerType { get; set; } = AutomationTriggerType.RecordCreated;

    /// <summary>JSON config for field-change or stage-change triggers.</summary>
    public JsonDocument? TriggerConfig { get; set; }

    /// <summary>A FilterGroup (JSON) evaluated against the entity in memory. Null matches all records.</summary>
    public JsonDocument? Conditions { get; set; }

    /// <summary>JSON array of AutomationActionDefinition.</summary>
    public JsonDocument Actions { get; set; } = JsonDocument.Parse("[]");

    public bool IsActive { get; set; } = true;

    /// <summary>Evaluation priority order (lower runs first).</summary>
    public int Priority { get; set; } = 1;
}

/// <summary>
/// Execution audit log for an automation rule.
/// </summary>
public sealed class AutomationExecutionLog : TenantEntity
{
    public Guid RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string TriggerType { get; set; } = string.Empty;

    /// <summary>Success, Failed, or Skipped.</summary>
    public string Status { get; set; } = "Success";

    public string? ErrorMessage { get; set; }
    public DateTime ExecutedAt { get; set; }
}
