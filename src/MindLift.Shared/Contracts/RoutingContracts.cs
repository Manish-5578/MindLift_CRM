namespace MindLift.Shared.Contracts;

public sealed class SaveTerritoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; } = 1;
    public string RoutingStrategy { get; set; } = "RoundRobin"; // RoundRobin, LoadBalanced, Weighted
    public string[] CountryCodes { get; set; } = [];
    public string[] Regions { get; set; } = [];
    public string[] Cities { get; set; } = [];
    public string[] PostalCodes { get; set; } = [];
    public string[] Industries { get; set; } = [];
    public decimal? MinRevenue { get; set; }
    public decimal? MaxRevenue { get; set; }
    public int? MinCompanySize { get; set; }
    public int? MaxCompanySize { get; set; }
    public Guid? FallbackUserId { get; set; }
    public List<SaveTerritoryMemberRequest> Members { get; set; } = [];
}

public sealed class SaveTerritoryMemberRequest
{
    public Guid UserId { get; set; }
    public int Weight { get; set; } = 1;
    public int? MaxCapacity { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TerritoryMemberDto
{
    public Guid Id { get; set; }
    public Guid TerritoryId { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }
    public int Weight { get; set; } = 1;
    public int? MaxCapacity { get; set; }
    public bool IsActive { get; set; }
    public int AssignedLeadCount { get; set; }
    public DateTime? LastAssignedAt { get; set; }
}

public sealed class TerritoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int Priority { get; set; }
    public string RoutingStrategy { get; set; } = "RoundRobin";
    public string[] CountryCodes { get; set; } = [];
    public string[] Regions { get; set; } = [];
    public string[] Cities { get; set; } = [];
    public string[] PostalCodes { get; set; } = [];
    public string[] Industries { get; set; } = [];
    public decimal? MinRevenue { get; set; }
    public decimal? MaxRevenue { get; set; }
    public int? MinCompanySize { get; set; }
    public int? MaxCompanySize { get; set; }
    public Guid? FallbackUserId { get; set; }
    public string? FallbackUserName { get; set; }
    public int MemberCount { get; set; }
    public List<TerritoryMemberDto> Members { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

public sealed class RouteLeadRequest
{
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? Industry { get; set; }
    public decimal? Revenue { get; set; }
    public int? CompanySize { get; set; }
    public Guid? FallbackUserId { get; set; }
}

public sealed class RouteLeadResultDto
{
    public Guid LeadId { get; set; }
    public Guid? TerritoryId { get; set; }
    public string? TerritoryName { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public string RoutingStrategy { get; set; } = "None";
    public string Reason { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTime RoutedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SimulateRoutingRequest
{
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? Industry { get; set; }
    public decimal? Revenue { get; set; }
    public int? CompanySize { get; set; }
}

public sealed class SimulateRoutingResultDto
{
    public bool Matched { get; set; }
    public Guid? MatchedTerritoryId { get; set; }
    public string? MatchedTerritoryName { get; set; }
    public List<string> MatchedCriteria { get; set; } = [];
    public Guid? CandidateUserId { get; set; }
    public string? CandidateUserName { get; set; }
    public string RoutingStrategy { get; set; } = "None";
    public string Reason { get; set; } = string.Empty;
}

public sealed class LeadRoutingLogDto
{
    public Guid Id { get; set; }
    public Guid LeadId { get; set; }
    public Guid? TerritoryId { get; set; }
    public string? TerritoryName { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string RoutingStrategy { get; set; } = "None";
    public string Reason { get; set; } = string.Empty;
    public bool Success { get; set; }
    public DateTime RoutedAt { get; set; }
}
