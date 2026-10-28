using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 4 / #22: Round-robin and territory routing (lead-routing).
/// A defined geographic or firmographic sales boundary with associated routing rules and sales reps.
/// </summary>
[Audited]
public sealed class Territory : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 1;

    /// <summary>
    /// Routing strategy: RoundRobin, LoadBalanced, or Weighted.
    /// </summary>
    public string RoutingStrategy { get; set; } = "RoundRobin";

    // Criteria for matching inbound leads
    public string[] CountryCodes { get; set; } = [];
    public string[] Regions { get; set; } = [];
    public string[] Cities { get; set; } = [];
    public string[] PostalCodes { get; set; } = [];
    public string[] Industries { get; set; } = [];
    public decimal? MinRevenue { get; set; }
    public decimal? MaxRevenue { get; set; }
    public int? MinCompanySize { get; set; }
    public int? MaxCompanySize { get; set; }

    // Fallback and assignment state
    public Guid? FallbackUserId { get; set; }
    public Guid? LastAssignedMemberId { get; set; }
    public int LastAssignedIndex { get; set; } = -1;

    public ICollection<TerritoryMember> Members { get; set; } = new List<TerritoryMember>();
}

/// <summary>
/// A sales rep assigned to a territory with distribution weights and capacity limits.
/// </summary>
[Audited]
public sealed class TerritoryMember : TenantEntity
{
    public Guid TerritoryId { get; set; }
    public Territory Territory { get; set; } = null!;
    public Guid UserId { get; set; }
    public int Weight { get; set; } = 1;
    public int? MaxCapacity { get; set; }
    public bool IsActive { get; set; } = true;
    public int AssignedLeadCount { get; set; }
    public DateTime? LastAssignedAt { get; set; }
}

/// <summary>
/// Immutable audit record of an automated or manual lead routing execution.
/// </summary>
[Audited]
public sealed class LeadRoutingLog : TenantEntity
{
    public Guid LeadId { get; set; }
    public Guid? TerritoryId { get; set; }
    public string? TerritoryName { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string RoutingStrategy { get; set; } = "None";
    public string Reason { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTime RoutedAt { get; set; } = DateTime.UtcNow;
}
