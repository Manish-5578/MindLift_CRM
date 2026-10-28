using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Automation;

/// <summary>
/// Phase 4 / #22: Round-robin and territory routing (lead-routing).
/// Evaluates inbound and existing leads against geographic, firmographic, and custom criteria
/// and distributes them to sales reps via RoundRobin, LoadBalanced, or Weighted strategies.
/// </summary>
public sealed class RoutingService(
    AppDbContext db,
    ITenantContext tenant,
    ILogger<RoutingService> logger) : IRoutingService
{
    // ================= Territory Management =================

    public async Task<TerritoryDto> CreateTerritoryAsync(SaveTerritoryRequest req, CancellationToken ct = default)
    {
        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Territory name is required.");

        var strategy = NormalizeStrategy(req.RoutingStrategy);

        var territory = new Territory
        {
            TenantId = tenant.TenantId ?? Guid.Empty,
            Name = name,
            Description = req.Description?.Trim(),
            IsActive = req.IsActive,
            Priority = req.Priority <= 0 ? 1 : req.Priority,
            RoutingStrategy = strategy,
            CountryCodes = CleanArray(req.CountryCodes),
            Regions = CleanArray(req.Regions),
            Cities = CleanArray(req.Cities),
            PostalCodes = CleanArray(req.PostalCodes),
            Industries = CleanArray(req.Industries),
            MinRevenue = req.MinRevenue,
            MaxRevenue = req.MaxRevenue,
            MinCompanySize = req.MinCompanySize,
            MaxCompanySize = req.MaxCompanySize,
            FallbackUserId = req.FallbackUserId,
            CreatedAt = DateTime.UtcNow
        };

        if (req.Members != null && req.Members.Count > 0)
        {
            foreach (var m in req.Members)
            {
                if (m.UserId == Guid.Empty) continue;
                territory.Members.Add(new TerritoryMember
                {
                    TenantId = territory.TenantId,
                    UserId = m.UserId,
                    Weight = m.Weight <= 0 ? 1 : m.Weight,
                    MaxCapacity = m.MaxCapacity,
                    IsActive = m.IsActive,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        db.Territories.Add(territory);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created territory {TerritoryId} ({TerritoryName}) with {MemberCount} members",
            territory.Id, territory.Name, territory.Members.Count);

        return await MapTerritoryDtoAsync(territory, ct);
    }

    public async Task<TerritoryDto> GetTerritoryByIdAsync(Guid id, CancellationToken ct = default)
    {
        var territory = await db.Territories
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (territory == null)
            throw new NotFoundException("Territory", id);

        return await MapTerritoryDtoAsync(territory, ct);
    }

    public async Task<IReadOnlyList<TerritoryDto>> ListTerritoriesAsync(bool? activeOnly = null, CancellationToken ct = default)
    {
        var query = db.Territories
            .Include(t => t.Members)
            .AsQueryable();

        if (activeOnly.HasValue)
            query = query.Where(t => t.IsActive == activeOnly.Value);

        var list = await query
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);

        var userIds = list.SelectMany(t => t.Members.Select(m => m.UserId))
            .Concat(list.Where(t => t.FallbackUserId.HasValue).Select(t => t.FallbackUserId!.Value))
            .Distinct()
            .ToList();

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.FirstName, u.LastName, u.Email }, ct);

        return list.Select(t =>
        {
            string? fallbackName = null;
            if (t.FallbackUserId.HasValue && users.TryGetValue(t.FallbackUserId.Value, out var fu))
                fallbackName = $"{fu.FirstName} {fu.LastName}".Trim();

            return new TerritoryDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                IsActive = t.IsActive,
                Priority = t.Priority,
                RoutingStrategy = t.RoutingStrategy,
                CountryCodes = t.CountryCodes,
                Regions = t.Regions,
                Cities = t.Cities,
                PostalCodes = t.PostalCodes,
                Industries = t.Industries,
                MinRevenue = t.MinRevenue,
                MaxRevenue = t.MaxRevenue,
                MinCompanySize = t.MinCompanySize,
                MaxCompanySize = t.MaxCompanySize,
                FallbackUserId = t.FallbackUserId,
                FallbackUserName = fallbackName,
                MemberCount = t.Members.Count,
                Members = t.Members.Select(m =>
                {
                    string? uName = null;
                    string? uEmail = null;
                    if (users.TryGetValue(m.UserId, out var mu))
                    {
                        uName = $"{mu.FirstName} {mu.LastName}".Trim();
                        uEmail = mu.Email;
                    }
                    return new TerritoryMemberDto
                    {
                        Id = m.Id,
                        TerritoryId = m.TerritoryId,
                        UserId = m.UserId,
                        UserName = uName,
                        UserEmail = uEmail,
                        Weight = m.Weight,
                        MaxCapacity = m.MaxCapacity,
                        IsActive = m.IsActive,
                        AssignedLeadCount = m.AssignedLeadCount,
                        LastAssignedAt = m.LastAssignedAt
                    };
                }).ToList(),
                CreatedAt = t.CreatedAt
            };
        }).ToList();
    }

    public async Task<TerritoryDto> UpdateTerritoryAsync(Guid id, SaveTerritoryRequest req, CancellationToken ct = default)
    {
        var territory = await db.Territories
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (territory == null)
            throw new NotFoundException("Territory", id);

        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Territory name is required.");

        territory.Name = name;
        territory.Description = req.Description?.Trim();
        territory.IsActive = req.IsActive;
        territory.Priority = req.Priority <= 0 ? 1 : req.Priority;
        territory.RoutingStrategy = NormalizeStrategy(req.RoutingStrategy);
        territory.CountryCodes = CleanArray(req.CountryCodes);
        territory.Regions = CleanArray(req.Regions);
        territory.Cities = CleanArray(req.Cities);
        territory.PostalCodes = CleanArray(req.PostalCodes);
        territory.Industries = CleanArray(req.Industries);
        territory.MinRevenue = req.MinRevenue;
        territory.MaxRevenue = req.MaxRevenue;
        territory.MinCompanySize = req.MinCompanySize;
        territory.MaxCompanySize = req.MaxCompanySize;
        territory.FallbackUserId = req.FallbackUserId;
        territory.UpdatedAt = DateTime.UtcNow;

        if (req.Members != null)
        {
            var reqUserIds = req.Members.Select(m => m.UserId).ToHashSet();

            // Remove members not in request
            var toRemove = territory.Members.Where(m => !reqUserIds.Contains(m.UserId)).ToList();
            foreach (var r in toRemove)
                db.TerritoryMembers.Remove(r);

            // Update or add members
            foreach (var rm in req.Members)
            {
                if (rm.UserId == Guid.Empty) continue;
                var existing = territory.Members.FirstOrDefault(m => m.UserId == rm.UserId);
                if (existing != null)
                {
                    existing.Weight = rm.Weight <= 0 ? 1 : rm.Weight;
                    existing.MaxCapacity = rm.MaxCapacity;
                    existing.IsActive = rm.IsActive;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    territory.Members.Add(new TerritoryMember
                    {
                        TenantId = territory.TenantId,
                        TerritoryId = territory.Id,
                        UserId = rm.UserId,
                        Weight = rm.Weight <= 0 ? 1 : rm.Weight,
                        MaxCapacity = rm.MaxCapacity,
                        IsActive = rm.IsActive,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return await MapTerritoryDtoAsync(territory, ct);
    }

    public async Task<bool> DeleteTerritoryAsync(Guid id, CancellationToken ct = default)
    {
        var territory = await db.Territories.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (territory == null) return false;

        territory.IsDeleted = true;
        territory.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ================= Member Management =================

    public async Task<TerritoryMemberDto> AddOrUpdateMemberAsync(Guid territoryId, SaveTerritoryMemberRequest req, CancellationToken ct = default)
    {
        var territory = await db.Territories.FirstOrDefaultAsync(t => t.Id == territoryId, ct);
        if (territory == null)
            throw new NotFoundException("Territory", territoryId);

        if (req.UserId == Guid.Empty)
            throw new ValidationException("UserId", "UserId is required.");

        var member = await db.TerritoryMembers.FirstOrDefaultAsync(m => m.TerritoryId == territoryId && m.UserId == req.UserId, ct);
        if (member == null)
        {
            member = new TerritoryMember
            {
                TenantId = territory.TenantId,
                TerritoryId = territory.Id,
                UserId = req.UserId,
                Weight = req.Weight <= 0 ? 1 : req.Weight,
                MaxCapacity = req.MaxCapacity,
                IsActive = req.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            db.TerritoryMembers.Add(member);
        }
        else
        {
            member.Weight = req.Weight <= 0 ? 1 : req.Weight;
            member.MaxCapacity = req.MaxCapacity;
            member.IsActive = req.IsActive;
            member.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == member.UserId, ct);
        return new TerritoryMemberDto
        {
            Id = member.Id,
            TerritoryId = member.TerritoryId,
            UserId = member.UserId,
            UserName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : null,
            UserEmail = user?.Email,
            Weight = member.Weight,
            MaxCapacity = member.MaxCapacity,
            IsActive = member.IsActive,
            AssignedLeadCount = member.AssignedLeadCount,
            LastAssignedAt = member.LastAssignedAt
        };
    }

    public async Task<bool> RemoveMemberAsync(Guid territoryId, Guid userId, CancellationToken ct = default)
    {
        var member = await db.TerritoryMembers.FirstOrDefaultAsync(m => m.TerritoryId == territoryId && m.UserId == userId, ct);
        if (member == null) return false;

        db.TerritoryMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<TerritoryMemberDto>> ListMembersAsync(Guid territoryId, CancellationToken ct = default)
    {
        var members = await db.TerritoryMembers
            .Where(m => m.TerritoryId == territoryId)
            .ToListAsync(ct);

        var userIds = members.Select(m => m.UserId).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.FirstName, u.LastName, u.Email }, ct);

        return members.Select(m =>
        {
            string? uName = null;
            string? uEmail = null;
            if (users.TryGetValue(m.UserId, out var u))
            {
                uName = $"{u.FirstName} {u.LastName}".Trim();
                uEmail = u.Email;
            }
            return new TerritoryMemberDto
            {
                Id = m.Id,
                TerritoryId = m.TerritoryId,
                UserId = m.UserId,
                UserName = uName,
                UserEmail = uEmail,
                Weight = m.Weight,
                MaxCapacity = m.MaxCapacity,
                IsActive = m.IsActive,
                AssignedLeadCount = m.AssignedLeadCount,
                LastAssignedAt = m.LastAssignedAt
            };
        }).ToList();
    }

    // ================= Lead Routing & Simulation =================

    public async Task<RouteLeadResultDto> RouteLeadAsync(Guid leadId, RouteLeadRequest? req = null, CancellationToken ct = default)
    {
        var lead = await db.Set<Lead>().FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead == null)
            throw new NotFoundException("Lead", leadId);

        // Resolve attributes from request or lead custom fields / standard fields
        var country = req?.Country ?? ExtractCustomFieldString(lead.CustomFields, "country", "Country", "country_code");
        var region = req?.Region ?? ExtractCustomFieldString(lead.CustomFields, "state", "State", "region", "Region");
        var city = req?.City ?? ExtractCustomFieldString(lead.CustomFields, "city", "City");
        var postalCode = req?.PostalCode ?? ExtractCustomFieldString(lead.CustomFields, "postal_code", "PostalCode", "zip", "Zip");
        var industry = req?.Industry ?? ExtractCustomFieldString(lead.CustomFields, "industry", "Industry");
        var revenue = req?.Revenue ?? ExtractCustomFieldDecimal(lead.CustomFields, "revenue", "annual_revenue", "AnnualRevenue");
        var companySize = req?.CompanySize ?? ExtractCustomFieldInt(lead.CustomFields, "company_size", "employees", "CompanySize", "Employees");

        var territories = await db.Territories
            .Include(t => t.Members)
            .Where(t => t.IsActive)
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);

        Territory? matchedTerritory = null;
        List<string> matchedCriteria = [];

        foreach (var t in territories)
        {
            if (EvaluateTerritoryMatch(t, country, region, city, postalCode, industry, revenue, companySize, out var criteria))
            {
                matchedTerritory = t;
                matchedCriteria = criteria;
                break;
            }
        }

        Guid? assignedUserId = null;
        string? assignedUserName = null;
        string strategyUsed = "None";
        string reason;
        bool success = false;

        if (matchedTerritory != null)
        {
            var eligibleMembers = matchedTerritory.Members.Where(m => m.IsActive).ToList();

            // Capacity filter: if MaxCapacity is defined, filter members at or over capacity
            if (eligibleMembers.Count > 1)
            {
                var underCapacity = eligibleMembers.Where(m => !m.MaxCapacity.HasValue || m.AssignedLeadCount < m.MaxCapacity.Value).ToList();
                if (underCapacity.Count > 0)
                    eligibleMembers = underCapacity;
            }

            if (eligibleMembers.Count > 0)
            {
                TerritoryMember? selectedMember = null;
                strategyUsed = matchedTerritory.RoutingStrategy;

                switch (strategyUsed)
                {
                    case "LoadBalanced":
                        // Rep with fewest active/open leads
                        var memberUserIds = eligibleMembers.Select(m => m.UserId).ToList();
                        var activeLeadCounts = await db.Set<Lead>()
                            .Where(l => memberUserIds.Contains(l.OwnerId) && l.Status != "converted" && l.Status != "disqualified")
                            .GroupBy(l => l.OwnerId)
                            .Select(g => new { UserId = g.Key, Count = g.Count() })
                            .ToDictionaryAsync(g => g.UserId, g => g.Count, ct);

                        selectedMember = eligibleMembers
                            .OrderBy(m => activeLeadCounts.GetValueOrDefault(m.UserId, 0))
                            .ThenBy(m => m.LastAssignedAt ?? DateTime.MinValue)
                            .First();
                        break;

                    case "Weighted":
                        // Proportional distribution: rep with lowest assigned/weight ratio
                        selectedMember = eligibleMembers
                            .OrderBy(m => (double)m.AssignedLeadCount / Math.Max(1, m.Weight))
                            .ThenByDescending(m => m.Weight)
                            .ThenBy(m => m.LastAssignedAt ?? DateTime.MinValue)
                            .First();
                        break;

                    case "RoundRobin":
                    default:
                        strategyUsed = "RoundRobin";
                        var sortedMembers = eligibleMembers.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();
                        int nextIdx = (matchedTerritory.LastAssignedIndex + 1) % sortedMembers.Count;
                        matchedTerritory.LastAssignedIndex = nextIdx;
                        selectedMember = sortedMembers[nextIdx];
                        break;
                }

                if (selectedMember != null)
                {
                    assignedUserId = selectedMember.UserId;
                    selectedMember.AssignedLeadCount++;
                    selectedMember.LastAssignedAt = DateTime.UtcNow;
                    matchedTerritory.LastAssignedMemberId = selectedMember.Id;
                    matchedTerritory.UpdatedAt = DateTime.UtcNow;
                    success = true;
                    reason = $"Matched territory '{matchedTerritory.Name}' (Priority {matchedTerritory.Priority}) via {strategyUsed} strategy. Criteria: [{string.Join(", ", matchedCriteria)}]";
                }
                else
                {
                    reason = $"Matched territory '{matchedTerritory.Name}' but could not select an eligible member.";
                }
            }
            else if (matchedTerritory.FallbackUserId.HasValue)
            {
                assignedUserId = matchedTerritory.FallbackUserId.Value;
                strategyUsed = "TerritoryFallback";
                success = true;
                reason = $"Matched territory '{matchedTerritory.Name}', but no active members were available. Assigned to territory fallback user.";
            }
            else
            {
                reason = $"Matched territory '{matchedTerritory.Name}', but no members or fallback user configured.";
            }
        }
        else if (req?.FallbackUserId.HasValue == true)
        {
            assignedUserId = req.FallbackUserId.Value;
            strategyUsed = "RequestFallback";
            success = true;
            reason = "No territory matched criteria. Assigned to request fallback user.";
        }
        else
        {
            reason = "No active territory matched the lead attributes and no fallback user was specified.";
        }

        if (assignedUserId.HasValue)
        {
            lead.OwnerId = assignedUserId.Value;
            lead.AssignedAt = DateTime.UtcNow;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == assignedUserId.Value, ct);
            if (user != null)
                assignedUserName = $"{user.FirstName} {user.LastName}".Trim();
        }

        var log = new LeadRoutingLog
        {
            TenantId = lead.TenantId,
            LeadId = lead.Id,
            TerritoryId = matchedTerritory?.Id,
            TerritoryName = matchedTerritory?.Name,
            AssignedUserId = assignedUserId,
            RoutingStrategy = strategyUsed,
            Reason = reason,
            Success = success,
            RoutedAt = DateTime.UtcNow
        };
        db.LeadRoutingLogs.Add(log);

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Lead {LeadId} routed: Success={Success}, Territory={TerritoryName}, Owner={OwnerId}, Strategy={Strategy}",
            lead.Id, success, matchedTerritory?.Name, assignedUserId, strategyUsed);

        return new RouteLeadResultDto
        {
            LeadId = lead.Id,
            TerritoryId = matchedTerritory?.Id,
            TerritoryName = matchedTerritory?.Name,
            AssignedUserId = assignedUserId,
            AssignedUserName = assignedUserName,
            RoutingStrategy = strategyUsed,
            Reason = reason,
            Success = success,
            RoutedAt = log.RoutedAt
        };
    }

    public async Task<SimulateRoutingResultDto> SimulateRoutingAsync(SimulateRoutingRequest req, CancellationToken ct = default)
    {
        var territories = await db.Territories
            .Include(t => t.Members)
            .Where(t => t.IsActive)
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);

        Territory? matchedTerritory = null;
        List<string> matchedCriteria = [];

        foreach (var t in territories)
        {
            if (EvaluateTerritoryMatch(t, req.Country, req.Region, req.City, req.PostalCode, req.Industry, req.Revenue, req.CompanySize, out var criteria))
            {
                matchedTerritory = t;
                matchedCriteria = criteria;
                break;
            }
        }

        if (matchedTerritory == null)
        {
            return new SimulateRoutingResultDto
            {
                Matched = false,
                Reason = "No active territory matched the provided criteria."
            };
        }

        var eligibleMembers = matchedTerritory.Members.Where(m => m.IsActive).ToList();
        Guid? candidateUserId = null;
        string? candidateUserName = null;
        string strategy = matchedTerritory.RoutingStrategy;
        string reason;

        if (eligibleMembers.Count > 0)
        {
            TerritoryMember? selectedMember = null;
            switch (strategy)
            {
                case "LoadBalanced":
                    var memberUserIds = eligibleMembers.Select(m => m.UserId).ToList();
                    var activeLeadCounts = await db.Set<Lead>()
                        .Where(l => memberUserIds.Contains(l.OwnerId) && l.Status != "converted" && l.Status != "disqualified")
                        .GroupBy(l => l.OwnerId)
                        .Select(g => new { UserId = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(g => g.UserId, g => g.Count, ct);

                    selectedMember = eligibleMembers
                        .OrderBy(m => activeLeadCounts.GetValueOrDefault(m.UserId, 0))
                        .ThenBy(m => m.LastAssignedAt ?? DateTime.MinValue)
                        .First();
                    break;

                case "Weighted":
                    selectedMember = eligibleMembers
                        .OrderBy(m => (double)m.AssignedLeadCount / Math.Max(1, m.Weight))
                        .ThenByDescending(m => m.Weight)
                        .ThenBy(m => m.LastAssignedAt ?? DateTime.MinValue)
                        .First();
                    break;

                case "RoundRobin":
                default:
                    strategy = "RoundRobin";
                    var sorted = eligibleMembers.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();
                    int nextIdx = (matchedTerritory.LastAssignedIndex + 1) % sorted.Count;
                    selectedMember = sorted[nextIdx];
                    break;
            }

            candidateUserId = selectedMember.UserId;
            reason = $"Simulated match on territory '{matchedTerritory.Name}'. Next recipient via {strategy} is user {candidateUserId}.";
        }
        else if (matchedTerritory.FallbackUserId.HasValue)
        {
            candidateUserId = matchedTerritory.FallbackUserId.Value;
            strategy = "TerritoryFallback";
            reason = $"Simulated match on territory '{matchedTerritory.Name}'. No active members; falls back to user {candidateUserId}.";
        }
        else
        {
            reason = $"Simulated match on territory '{matchedTerritory.Name}', but no members or fallback user configured.";
        }

        if (candidateUserId.HasValue)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == candidateUserId.Value, ct);
            if (user != null) candidateUserName = $"{user.FirstName} {user.LastName}".Trim();
        }

        return new SimulateRoutingResultDto
        {
            Matched = true,
            MatchedTerritoryId = matchedTerritory.Id,
            MatchedTerritoryName = matchedTerritory.Name,
            MatchedCriteria = matchedCriteria,
            CandidateUserId = candidateUserId,
            CandidateUserName = candidateUserName,
            RoutingStrategy = strategy,
            Reason = reason
        };
    }

    public async Task<IReadOnlyList<LeadRoutingLogDto>> ListRoutingLogsAsync(Guid? leadId = null, Guid? territoryId = null, int limit = 50, CancellationToken ct = default)
    {
        var query = db.LeadRoutingLogs.AsQueryable();

        if (leadId.HasValue)
            query = query.Where(l => l.LeadId == leadId.Value);

        if (territoryId.HasValue)
            query = query.Where(l => l.TerritoryId == territoryId.Value);

        var list = await query
            .OrderByDescending(l => l.RoutedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        return list.Select(l => new LeadRoutingLogDto
        {
            Id = l.Id,
            LeadId = l.LeadId,
            TerritoryId = l.TerritoryId,
            TerritoryName = l.TerritoryName,
            AssignedUserId = l.AssignedUserId,
            RoutingStrategy = l.RoutingStrategy,
            Reason = l.Reason,
            Success = l.Success,
            RoutedAt = l.RoutedAt
        }).ToList();
    }

    // ================= Helper Methods =================

    private static bool EvaluateTerritoryMatch(
        Territory t,
        string? country,
        string? region,
        string? city,
        string? postalCode,
        string? industry,
        decimal? revenue,
        int? companySize,
        out List<string> matchedCriteria)
    {
        matchedCriteria = [];

        // 1. Country Codes
        if (t.CountryCodes.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(country) || !t.CountryCodes.Any(c => string.Equals(c.Trim(), country.Trim(), StringComparison.OrdinalIgnoreCase)))
                return false;
            matchedCriteria.Add($"Country: {country}");
        }

        // 2. Regions
        if (t.Regions.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(region) || !t.Regions.Any(r => string.Equals(r.Trim(), region.Trim(), StringComparison.OrdinalIgnoreCase)))
                return false;
            matchedCriteria.Add($"Region: {region}");
        }

        // 3. Cities
        if (t.Cities.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(city) || !t.Cities.Any(c => string.Equals(c.Trim(), city.Trim(), StringComparison.OrdinalIgnoreCase)))
                return false;
            matchedCriteria.Add($"City: {city}");
        }

        // 4. Postal Codes (supports wildcard prefix like '94*')
        if (t.PostalCodes.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(postalCode))
                return false;

            var cleanPostal = postalCode.Trim();
            bool postalMatch = t.PostalCodes.Any(pc =>
            {
                var rule = pc.Trim();
                if (rule.EndsWith('*'))
                {
                    var prefix = rule.TrimEnd('*');
                    return cleanPostal.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }
                return string.Equals(rule, cleanPostal, StringComparison.OrdinalIgnoreCase);
            });

            if (!postalMatch) return false;
            matchedCriteria.Add($"PostalCode: {postalCode}");
        }

        // 5. Industries
        if (t.Industries.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(industry) || !t.Industries.Any(i => string.Equals(i.Trim(), industry.Trim(), StringComparison.OrdinalIgnoreCase)))
                return false;
            matchedCriteria.Add($"Industry: {industry}");
        }

        // 6. Revenue Range
        if (t.MinRevenue.HasValue)
        {
            if (!revenue.HasValue || revenue.Value < t.MinRevenue.Value)
                return false;
            matchedCriteria.Add($"MinRevenue: {t.MinRevenue}");
        }
        if (t.MaxRevenue.HasValue)
        {
            if (!revenue.HasValue || revenue.Value > t.MaxRevenue.Value)
                return false;
            matchedCriteria.Add($"MaxRevenue: {t.MaxRevenue}");
        }

        // 7. Company Size Range
        if (t.MinCompanySize.HasValue)
        {
            if (!companySize.HasValue || companySize.Value < t.MinCompanySize.Value)
                return false;
            matchedCriteria.Add($"MinCompanySize: {t.MinCompanySize}");
        }
        if (t.MaxCompanySize.HasValue)
        {
            if (!companySize.HasValue || companySize.Value > t.MaxCompanySize.Value)
                return false;
            matchedCriteria.Add($"MaxCompanySize: {t.MaxCompanySize}");
        }

        if (matchedCriteria.Count == 0)
            matchedCriteria.Add("DefaultCatchAll");

        return true;
    }

    private static string NormalizeStrategy(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy)) return "RoundRobin";
        return strategy.Trim().ToLowerInvariant() switch
        {
            "loadbalanced" => "LoadBalanced",
            "weighted" => "Weighted",
            _ => "RoundRobin"
        };
    }

    private static string[] CleanArray(string[]? input)
    {
        if (input == null || input.Length == 0) return [];
        return input
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ExtractCustomFieldString(JsonDocument? doc, params string[] propertyNames)
    {
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (doc.RootElement.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.ToString();
            }
        }
        return null;
    }

    private static decimal? ExtractCustomFieldDecimal(JsonDocument? doc, params string[] propertyNames)
    {
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (doc.RootElement.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var val))
                    return val;
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var parsed))
                    return parsed;
            }
        }
        return null;
    }

    private static int? ExtractCustomFieldInt(JsonDocument? doc, params string[] propertyNames)
    {
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (doc.RootElement.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
                    return val;
                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
                    return parsed;
            }
        }
        return null;
    }

    private async Task<TerritoryDto> MapTerritoryDtoAsync(Territory territory, CancellationToken ct)
    {
        var userIds = territory.Members.Select(m => m.UserId)
            .Concat(territory.FallbackUserId.HasValue ? [territory.FallbackUserId.Value] : Enumerable.Empty<Guid>())
            .Distinct()
            .ToList();

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.FirstName, u.LastName, u.Email }, ct);

        string? fallbackName = null;
        if (territory.FallbackUserId.HasValue && users.TryGetValue(territory.FallbackUserId.Value, out var fu))
            fallbackName = $"{fu.FirstName} {fu.LastName}".Trim();

        return new TerritoryDto
        {
            Id = territory.Id,
            Name = territory.Name,
            Description = territory.Description,
            IsActive = territory.IsActive,
            Priority = territory.Priority,
            RoutingStrategy = territory.RoutingStrategy,
            CountryCodes = territory.CountryCodes,
            Regions = territory.Regions,
            Cities = territory.Cities,
            PostalCodes = territory.PostalCodes,
            Industries = territory.Industries,
            MinRevenue = territory.MinRevenue,
            MaxRevenue = territory.MaxRevenue,
            MinCompanySize = territory.MinCompanySize,
            MaxCompanySize = territory.MaxCompanySize,
            FallbackUserId = territory.FallbackUserId,
            FallbackUserName = fallbackName,
            MemberCount = territory.Members.Count,
            Members = territory.Members.Select(m =>
            {
                string? uName = null;
                string? uEmail = null;
                if (users.TryGetValue(m.UserId, out var mu))
                {
                    uName = $"{mu.FirstName} {mu.LastName}".Trim();
                    uEmail = mu.Email;
                }
                return new TerritoryMemberDto
                {
                    Id = m.Id,
                    TerritoryId = m.TerritoryId,
                    UserId = m.UserId,
                    UserName = uName,
                    UserEmail = uEmail,
                    Weight = m.Weight,
                    MaxCapacity = m.MaxCapacity,
                    IsActive = m.IsActive,
                    AssignedLeadCount = m.AssignedLeadCount,
                    LastAssignedAt = m.LastAssignedAt
                };
            }).ToList(),
            CreatedAt = territory.CreatedAt
        };
    }
}
