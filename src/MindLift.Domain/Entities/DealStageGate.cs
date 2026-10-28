using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// A gating rule that restricts when a Deal may enter a specific pipeline stage.
/// Gating rules can require fields, minimum amounts, attached line items, role restrictions,
/// or custom conditions evaluated with the F2 rules engine.
/// </summary>
[Audited]
public sealed class DealStageGate : TenantEntity
{
    public Guid PipelineId { get; set; }
    public Guid StageId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Required field names (e.g. account_id, primary_contact_id, expected_close_date, cf_*).</summary>
    public string[] RequiredFields { get; set; } = [];

    public decimal? MinAmount { get; set; }
    public bool RequireLineItems { get; set; }

    /// <summary>Serialized RuleNode evaluated against deal facts.</summary>
    public string? ConditionJson { get; set; }

    /// <summary>Allowed user roles (e.g. "Admin", "Manager"). Empty means any role can transition.</summary>
    public string[] AllowedRoles { get; set; } = [];

    /// <summary>Custom error message displayed when gating fails.</summary>
    public string? FailureMessage { get; set; }
}
