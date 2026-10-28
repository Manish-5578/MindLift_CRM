using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Deals;

/// <summary>
/// Phase 3 / #17: Competitor Tracking (competitors).
/// Service for managing competitor battlecards, tracking competitors on deals, and competitive win rate analysis.
/// </summary>
public sealed class CompetitorService(
    AppDbContext db,
    ITenantContext tenant) : ICompetitorService
{
    private static readonly HashSet<string> ValidThreatLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Low", "Medium", "High"
    };

    public async Task<IReadOnlyList<CompetitorDto>> ListCompetitorsAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = db.Competitors.AsNoTracking().AsQueryable();
        if (activeOnly)
            query = query.Where(c => c.IsActive);

        var competitors = await query.OrderBy(c => c.Name).ToListAsync(cancellationToken);
        return await ComputeCompetitorDtosAsync(competitors, cancellationToken);
    }

    public async Task<CompetitorDto> GetCompetitorByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var competitor = await db.Competitors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Competitor", id);

        var dtos = await ComputeCompetitorDtosAsync([competitor], cancellationToken);
        return dtos[0];
    }

    public async Task<CompetitorDto> CreateCompetitorAsync(SaveCompetitorRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Competitor name is required.");

        var exists = await db.Competitors.AnyAsync(c => c.Name.ToLower() == name.ToLower(), cancellationToken);
        if (exists)
            throw new ValidationException("Name", $"A competitor named '{name}' already exists.");

        var competitor = new Competitor
        {
            Name = name,
            Website = request.Website?.Trim(),
            Strengths = request.Strengths?.Trim(),
            Weaknesses = request.Weaknesses?.Trim(),
            Battlecard = request.Battlecard?.Trim(),
            PricingStrategy = request.PricingStrategy?.Trim(),
            IsActive = request.IsActive
        };

        db.Competitors.Add(competitor);
        await db.SaveChangesAsync(cancellationToken);

        return await GetCompetitorByIdAsync(competitor.Id, cancellationToken);
    }

    public async Task<CompetitorDto> UpdateCompetitorAsync(Guid id, SaveCompetitorRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Competitor name is required.");

        var competitor = await db.Competitors.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Competitor", id);

        var duplicate = await db.Competitors.AnyAsync(c => c.Id != id && c.Name.ToLower() == name.ToLower(), cancellationToken);
        if (duplicate)
            throw new ValidationException("Name", $"Another competitor named '{name}' already exists.");

        competitor.Name = name;
        competitor.Website = request.Website?.Trim();
        competitor.Strengths = request.Strengths?.Trim();
        competitor.Weaknesses = request.Weaknesses?.Trim();
        competitor.Battlecard = request.Battlecard?.Trim();
        competitor.PricingStrategy = request.PricingStrategy?.Trim();
        competitor.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);
        return await GetCompetitorByIdAsync(id, cancellationToken);
    }

    public async Task DeleteCompetitorAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var competitor = await db.Competitors.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Competitor", id);

        db.Competitors.Remove(competitor);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DealCompetitorDto>> GetDealCompetitorsAsync(Guid dealId, CancellationToken cancellationToken = default)
    {
        var dealExists = await db.Deals.AnyAsync(d => d.Id == dealId, cancellationToken);
        if (!dealExists)
            throw new NotFoundException("Deal", dealId);

        var associations = await db.DealCompetitors.AsNoTracking()
            .Where(dc => dc.DealId == dealId)
            .Include(dc => dc.Competitor)
            .OrderByDescending(dc => dc.ThreatLevel)
            .ThenBy(dc => dc.Competitor != null ? dc.Competitor.Name : string.Empty)
            .ToListAsync(cancellationToken);

        return associations.Select(MapDealCompetitor).ToList();
    }

    public async Task<DealCompetitorDto> AddCompetitorToDealAsync(Guid dealId, AddDealCompetitorRequest request, CancellationToken cancellationToken = default)
    {
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId, cancellationToken)
            ?? throw new NotFoundException("Deal", dealId);

        var competitor = await db.Competitors.FirstOrDefaultAsync(c => c.Id == request.CompetitorId, cancellationToken)
            ?? throw new NotFoundException("Competitor", request.CompetitorId);

        var threatLevel = NormalizeThreatLevel(request.ThreatLevel);

        var alreadyAssociated = await db.DealCompetitors.AnyAsync(
            dc => dc.DealId == dealId && dc.CompetitorId == request.CompetitorId, cancellationToken);

        if (alreadyAssociated)
            throw new ValidationException("CompetitorId", "This competitor is already associated with this deal.");

        var association = new DealCompetitor
        {
            DealId = dealId,
            CompetitorId = request.CompetitorId,
            ThreatLevel = threatLevel,
            StrengthsObserved = request.StrengthsObserved?.Trim(),
            WeaknessesObserved = request.WeaknessesObserved?.Trim(),
            Notes = request.Notes?.Trim()
        };

        db.DealCompetitors.Add(association);
        await db.SaveChangesAsync(cancellationToken);

        association.Competitor = competitor;
        return MapDealCompetitor(association);
    }

    public async Task<DealCompetitorDto> UpdateDealCompetitorAsync(
        Guid dealId,
        Guid competitorId,
        UpdateDealCompetitorRequest request,
        CancellationToken cancellationToken = default)
    {
        var association = await db.DealCompetitors
            .Include(dc => dc.Competitor)
            .FirstOrDefaultAsync(dc => dc.DealId == dealId && dc.CompetitorId == competitorId, cancellationToken)
            ?? throw new NotFoundException("DealCompetitor", $"{dealId}/{competitorId}");

        association.ThreatLevel = NormalizeThreatLevel(request.ThreatLevel);
        association.StrengthsObserved = request.StrengthsObserved?.Trim();
        association.WeaknessesObserved = request.WeaknessesObserved?.Trim();
        association.Notes = request.Notes?.Trim();

        await db.SaveChangesAsync(cancellationToken);
        return MapDealCompetitor(association);
    }

    public async Task RemoveCompetitorFromDealAsync(Guid dealId, Guid competitorId, CancellationToken cancellationToken = default)
    {
        var association = await db.DealCompetitors
            .FirstOrDefaultAsync(dc => dc.DealId == dealId && dc.CompetitorId == competitorId, cancellationToken)
            ?? throw new NotFoundException("DealCompetitor", $"{dealId}/{competitorId}");

        db.DealCompetitors.Remove(association);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CompetitorAnalyticsSummaryDto> GetCompetitorAnalyticsAsync(CancellationToken cancellationToken = default)
    {
        var competitors = await ListCompetitorsAsync(activeOnly: false, cancellationToken);
        var totalDealsWithCompetitors = await db.DealCompetitors.AsNoTracking()
            .Select(dc => dc.DealId)
            .Distinct()
            .CountAsync(cancellationToken);

        var totalWonDeals = competitors.Sum(c => c.WonDealsCount);
        var totalClosedDeals = competitors.Sum(c => c.WonDealsCount + c.LostDealsCount);
        var overallWinRate = totalClosedDeals > 0
            ? Math.Round((totalWonDeals / (decimal)totalClosedDeals) * 100m, 2)
            : 0m;

        return new CompetitorAnalyticsSummaryDto(
            TotalCompetitors: competitors.Count,
            TotalDealsWithCompetitors: totalDealsWithCompetitors,
            OverallWinRateAgainstCompetitors: overallWinRate,
            Competitors: competitors
        );
    }

    private async Task<IReadOnlyList<CompetitorDto>> ComputeCompetitorDtosAsync(
        List<Competitor> competitors,
        CancellationToken cancellationToken)
    {
        if (competitors.Count == 0) return [];

        var competitorIds = competitors.Select(c => c.Id).ToList();

        var dealAssociations = await db.DealCompetitors.AsNoTracking()
            .Where(dc => competitorIds.Contains(dc.CompetitorId))
            .Select(dc => new
            {
                dc.CompetitorId,
                dc.DealId
            })
            .ToListAsync(cancellationToken);

        var dealIds = dealAssociations.Select(da => da.DealId).Distinct().ToList();

        var deals = await db.Deals.AsNoTracking()
            .Where(d => dealIds.Contains(d.Id))
            .Select(d => new
            {
                d.Id,
                d.Status,
                d.Amount
            })
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var associationsByCompetitor = dealAssociations
            .GroupBy(da => da.CompetitorId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DealId).Distinct().ToList());

        var result = new List<CompetitorDto>();

        foreach (var comp in competitors)
        {
            var associatedDealIds = associationsByCompetitor.GetValueOrDefault(comp.Id, []);
            var compDeals = associatedDealIds.Select(id => deals.GetValueOrDefault(id)).Where(d => d is not null).ToList();

            var totalDeals = compDeals.Count;
            var wonDeals = compDeals.Where(d => string.Equals(d!.Status, "Won", StringComparison.OrdinalIgnoreCase)).ToList();
            var lostDeals = compDeals.Where(d => string.Equals(d!.Status, "Lost", StringComparison.OrdinalIgnoreCase)).ToList();

            var wonCount = wonDeals.Count;
            var lostCount = lostDeals.Count;
            var closedCount = wonCount + lostCount;

            var winRate = closedCount > 0
                ? Math.Round((wonCount / (decimal)closedCount) * 100m, 2)
                : 0m;

            var wonRevenue = wonDeals.Sum(d => d!.Amount);
            var lostRevenue = lostDeals.Sum(d => d!.Amount);

            result.Add(new CompetitorDto(
                Id: comp.Id,
                Name: comp.Name,
                Website: comp.Website,
                Strengths: comp.Strengths,
                Weaknesses: comp.Weaknesses,
                Battlecard: comp.Battlecard,
                PricingStrategy: comp.PricingStrategy,
                IsActive: comp.IsActive,
                TotalDealsCount: totalDeals,
                WonDealsCount: wonCount,
                LostDealsCount: lostCount,
                WinRatePercentage: winRate,
                WonRevenue: wonRevenue,
                LostRevenue: lostRevenue
            ));
        }

        return result;
    }

    private static string NormalizeThreatLevel(string? input)
    {
        var trimmed = input?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !ValidThreatLevels.Contains(trimmed))
            return "Medium";

        return ValidThreatLevels.First(v => string.Equals(v, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static DealCompetitorDto MapDealCompetitor(DealCompetitor dc)
        => new(
            DealId: dc.DealId,
            CompetitorId: dc.CompetitorId,
            CompetitorName: dc.Competitor?.Name ?? "Unknown Competitor",
            Website: dc.Competitor?.Website,
            ThreatLevel: dc.ThreatLevel,
            StrengthsObserved: dc.StrengthsObserved,
            WeaknessesObserved: dc.WeaknessesObserved,
            Notes: dc.Notes,
            CreatedAtUtc: dc.CreatedAt
        );
}
