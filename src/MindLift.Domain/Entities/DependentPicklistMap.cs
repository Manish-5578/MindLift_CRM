using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 2 / #9: Dependent Picklists (dependent-picklists).
/// Maps allowed child picklist values based on the selected parent picklist value for an entity.
/// </summary>
[Audited]
public sealed class DependentPicklistMap : TenantEntity
{
    /// <summary>Entity type (e.g. "leads", "contacts", "accounts", "deals") or empty for all entities.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Controlling / parent picklist field or key (e.g. "country", "industry", "lead_source").</summary>
    public string ParentField { get; set; } = string.Empty;

    /// <summary>Dependent / child picklist field or key (e.g. "state", "sub_industry").</summary>
    public string ChildField { get; set; } = string.Empty;

    /// <summary>Human-readable name of this dependency rule.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// JSON dictionary mapping parent value codes to arrays of allowed child value codes.
    /// Example: { "usa": ["ca", "ny", "tx"], "uae": ["dxb", "auh", "shj"] }
    /// </summary>
    public string MappingJson { get; set; } = "{}";
}
