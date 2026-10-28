using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 4 / #28: Automated data enrichment (data-enrichment).
/// Audit trail for all automated firmographic and demographic enrichment runs.
/// </summary>
[Audited]
public sealed class EnrichmentLog : TenantEntity
{
    public string EntityType { get; set; } = "Lead"; // "Lead", "Account", "Contact"
    public Guid EntityId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Status { get; set; } = "Enriched"; // "Enriched", "NoMatch", "Failed"
    public string Provider { get; set; } = "MindLift Intelligence";
    public string EnrichedDataJson { get; set; } = "{}";
    public string[] FieldsUpdated { get; set; } = [];
    public double ConfidenceScore { get; set; } = 1.0;
    public DateTime EnrichedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tenant-level configuration for automated firmographic data enrichment.
/// </summary>
[Audited]
public sealed class EnrichmentConfig : TenantEntity
{
    public bool AutoEnrichNewLeads { get; set; } = true;
    public bool AutoEnrichNewAccounts { get; set; } = true;
    public bool OverwriteExistingFields { get; set; }
    public string DefaultProvider { get; set; } = "MindLift Intelligence";
    public string[] ExcludedDomains { get; set; } = ["gmail.com", "yahoo.com", "hotmail.com", "outlook.com", "icloud.com"];
}
