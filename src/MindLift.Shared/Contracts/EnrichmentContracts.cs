namespace MindLift.Shared.Contracts;

public sealed class CompanyEnrichmentData
{
    public string Domain { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Industry { get; set; }
    public int? EmployeeCount { get; set; }
    public string? EmployeeRange { get; set; }
    public decimal? EstimatedRevenueUsd { get; set; }
    public string? Description { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? LogoUrl { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string[] TechStack { get; set; } = [];
    public string? LinkedInUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public double Confidence { get; set; } = 1.0;
}

public sealed class EnrichmentResultDto
{
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = "Lead";
    public string Status { get; set; } = "Enriched";
    public string[] FieldsUpdated { get; set; } = [];
    public CompanyEnrichmentData? Data { get; set; }
    public DateTime EnrichedAt { get; set; } = DateTime.UtcNow;
}

public sealed class EnrichmentLogDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EntityType { get; set; } = "Lead";
    public Guid EntityId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Status { get; set; } = "Enriched";
    public string Provider { get; set; } = "MindLift Intelligence";
    public string[] FieldsUpdated { get; set; } = [];
    public double ConfidenceScore { get; set; }
    public DateTime EnrichedAt { get; set; }
}

public sealed class EnrichmentConfigDto
{
    public bool AutoEnrichNewLeads { get; set; }
    public bool AutoEnrichNewAccounts { get; set; }
    public bool OverwriteExistingFields { get; set; }
    public string DefaultProvider { get; set; } = "MindLift Intelligence";
    public string[] ExcludedDomains { get; set; } = [];
}

public sealed class SaveEnrichmentConfigRequest
{
    public bool AutoEnrichNewLeads { get; set; }
    public bool AutoEnrichNewAccounts { get; set; }
    public bool OverwriteExistingFields { get; set; }
    public string[]? ExcludedDomains { get; set; }
}

public sealed class BatchEnrichRequest
{
    public string EntityType { get; set; } = "Lead"; // "Lead", "Account"
    public List<Guid> EntityIds { get; set; } = [];
    public bool Overwrite { get; set; }
}
