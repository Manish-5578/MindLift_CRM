using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 4 / #23: Lead scoring (lead-scoring).
/// Configurable rules for evaluating inbound leads based on demographic attributes,
/// behavioral engagement, or time decay.
/// </summary>
[Audited]
public sealed class LeadScoringRule : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Rule category: Demographic, Behavioral, Decay, or Custom.
    /// </summary>
    public string RuleType { get; set; } = "Demographic";

    /// <summary>
    /// Point adjustment to apply when condition matches (e.g. +20, -10).
    /// </summary>
    public int Points { get; set; }

    /// <summary>
    /// The field or event evaluated: Industry, Source, AnnualRevenue, CompanySize,
    /// HasEmail, HasPhone, HasCompany, InactivityDays, CadenceTouch, etc.
    /// </summary>
    public string ConditionField { get; set; } = string.Empty;

    /// <summary>
    /// Operator: Equals, NotEquals, Contains, GreaterThan, LessThan, InList, Occurred.
    /// </summary>
    public string Operator { get; set; } = "Equals";

    /// <summary>
    /// The target value to compare against.
    /// </summary>
    public string ConditionValue { get; set; } = string.Empty;

    /// <summary>
    /// Priority order of rule evaluation.
    /// </summary>
    public int Order { get; set; } = 1;
}

/// <summary>
/// Immutable audit log of individual scoring rule executions against a lead.
/// </summary>
[Audited]
public sealed class LeadScoreLog : TenantEntity
{
    public Guid LeadId { get; set; }
    public Guid? RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public int Points { get; set; }
    public int RunningScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime ScoredAt { get; set; } = DateTime.UtcNow;
}
