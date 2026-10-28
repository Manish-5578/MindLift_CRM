using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 4 / #24: First-response SLA and escalation (response-sla).
/// Configurable response-time targets and automated escalation workflows for inbound leads.
/// </summary>
[Audited]
public sealed class SlaPolicy : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 1;

    /// <summary>
    /// Target maximum response time from assignment in minutes (e.g. 15, 60, 240).
    /// </summary>
    public int MaxResponseMinutes { get; set; } = 60;

    /// <summary>
    /// Threshold percentage to trigger early warning notifications (e.g. 80%).
    /// </summary>
    public int WarningThresholdPercentage { get; set; } = 80;

    /// <summary>
    /// Lead sources this policy applies to (e.g. ["website", "chat"]). Empty applies to all.
    /// </summary>
    public string[] Sources { get; set; } = [];

    /// <summary>
    /// Lead ratings this policy applies to (e.g. ["hot"]). Empty applies to all.
    /// </summary>
    public string[] Ratings { get; set; } = [];

    /// <summary>
    /// User to whom the lead is escalated if breached.
    /// </summary>
    public Guid? EscalationUserId { get; set; }

    /// <summary>
    /// Escalation action: NotifyOnly, Reassign, or ReassignAndNotify.
    /// </summary>
    public string EscalationAction { get; set; } = "ReassignAndNotify";
}

/// <summary>
/// Tracks first response SLA state and breach escalation for a specific lead.
/// </summary>
[Audited]
public sealed class LeadSlaTracker : TenantEntity
{
    public Guid LeadId { get; set; }
    public Guid PolicyId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public DateTime DueAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? WarningNotifiedAt { get; set; }
    public DateTime? BreachedAt { get; set; }

    /// <summary>
    /// SLA Status: Pending, Met, or Breached.
    /// </summary>
    public string Status { get; set; } = "Pending";

    public bool Escalated { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public Guid? EscalatedToUserId { get; set; }
    public string? Notes { get; set; }
}
