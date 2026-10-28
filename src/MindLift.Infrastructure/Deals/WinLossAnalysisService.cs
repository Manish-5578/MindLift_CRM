using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Deals;

/// <summary>
/// Phase 3 / #18: Win/Loss Analysis (win-loss).
/// Service for analyzing win/loss reasons, competitor correlations, sales cycle durations, and pipeline leakage.
/// </summary>
public sealed class WinLossAnalysisService(
    AppDbContext db,
    ITenantContext tenant) : IWinLossAnalysisService
{
    public async Task<WinLossSummaryDto> GetSummaryAsync(WinLossFilterQuery query, CancellationToken cancellationToken = default)
    {
        var deals = await GetClosedDealsAsync(query, cancellationToken);
        var reasons = ComputeReasonsBreakdown(deals);
        var competitors = await ComputeCompetitorCorrelationAsync(deals, cancellationToken);
        var stages = await ComputePipelineLeakageAsync(deals, cancellationToken);
        var reps = await ComputeSalesRepPerformanceAsync(deals, cancellationToken);

        var wonDeals = deals.Where(d => string.Equals(d.Status, "Won", StringComparison.OrdinalIgnoreCase)).ToList();
        var lostDeals = deals.Where(d => string.Equals(d.Status, "Lost", StringComparison.OrdinalIgnoreCase)).ToList();

        var totalClosed = deals.Count;
        var wonCount = wonDeals.Count;
        var lostCount = lostDeals.Count;

        var wonRevenue = wonDeals.Sum(d => d.Amount);
        var lostRevenue = lostDeals.Sum(d => d.Amount);

        var winRate = totalClosed > 0
            ? Math.Round((wonCount / (decimal)totalClosed) * 100m, 2)
            : 0m;

        var avgWonSize = wonCount > 0 ? Math.Round(wonRevenue / wonCount, 2) : 0m;
        var avgLostSize = lostCount > 0 ? Math.Round(lostRevenue / lostCount, 2) : 0m;

        var avgDaysToWin = wonCount > 0
            ? Math.Round((decimal)wonDeals.Average(d => Math.Max(0, ((d.ClosedAt ?? d.CreatedAt) - d.CreatedAt).TotalDays)), 1)
            : 0m;

        var avgDaysToLose = lostCount > 0
            ? Math.Round((decimal)lostDeals.Average(d => Math.Max(0, ((d.ClosedAt ?? d.CreatedAt) - d.CreatedAt).TotalDays)), 1)
            : 0m;

        return new WinLossSummaryDto(
            TotalClosedDeals: totalClosed,
            WonDealsCount: wonCount,
            LostDealsCount: lostCount,
            OverallWinRatePercentage: winRate,
            WonRevenue: wonRevenue,
            LostRevenue: lostRevenue,
            AverageWonDealSize: avgWonSize,
            AverageLostDealSize: avgLostSize,
            AverageDaysToWin: avgDaysToWin,
            AverageDaysToLose: avgDaysToLose,
            Reasons: reasons,
            Competitors: competitors,
            PipelineLeakage: stages,
            SalesReps: reps
        );
    }

    public async Task<WinLossReasonsBreakdownDto> GetReasonsBreakdownAsync(WinLossFilterQuery query, CancellationToken cancellationToken = default)
    {
        var deals = await GetClosedDealsAsync(query, cancellationToken);
        return ComputeReasonsBreakdown(deals);
    }

    public async Task<IReadOnlyList<CompetitorWinLossDto>> GetCompetitorCorrelationAsync(WinLossFilterQuery query, CancellationToken cancellationToken = default)
    {
        var deals = await GetClosedDealsAsync(query, cancellationToken);
        return await ComputeCompetitorCorrelationAsync(deals, cancellationToken);
    }

    public async Task<IReadOnlyList<StageLeakageDto>> GetPipelineLeakageAsync(WinLossFilterQuery query, CancellationToken cancellationToken = default)
    {
        var deals = await GetClosedDealsAsync(query, cancellationToken);
        return await ComputePipelineLeakageAsync(deals, cancellationToken);
    }

    public async Task<IReadOnlyList<RepWinLossDto>> GetSalesRepPerformanceAsync(WinLossFilterQuery query, CancellationToken cancellationToken = default)
    {
        var deals = await GetClosedDealsAsync(query, cancellationToken);
        return await ComputeSalesRepPerformanceAsync(deals, cancellationToken);
    }

    private async Task<List<Deal>> GetClosedDealsAsync(WinLossFilterQuery query, CancellationToken cancellationToken)
    {
        var dealQuery = db.Deals.AsNoTracking()
            .Where(d => d.Status == "Won" || d.Status == "Lost");

        if (query.PipelineId.HasValue)
            dealQuery = dealQuery.Where(d => d.PipelineId == query.PipelineId.Value);

        if (query.OwnerId.HasValue)
            dealQuery = dealQuery.Where(d => d.OwnerId == query.OwnerId.Value);

        var list = await dealQuery.ToListAsync(cancellationToken);

        if (query.StartDate.HasValue || query.EndDate.HasValue)
        {
            list = list.Where(d =>
            {
                var effective = d.ClosedAt ?? d.ExpectedCloseDate ?? d.CreatedAt;
                if (query.StartDate.HasValue && effective < query.StartDate.Value) return false;
                if (query.EndDate.HasValue && effective > query.EndDate.Value) return false;
                return true;
            }).ToList();
        }

        return list;
    }

    private static WinLossReasonsBreakdownDto ComputeReasonsBreakdown(List<Deal> deals)
    {
        var wonDeals = deals.Where(d => string.Equals(d.Status, "Won", StringComparison.OrdinalIgnoreCase)).ToList();
        var lostDeals = deals.Where(d => string.Equals(d.Status, "Lost", StringComparison.OrdinalIgnoreCase)).ToList();

        var winReasons = wonDeals
            .GroupBy(d => string.IsNullOrWhiteSpace(d.WinReason) ? "Unspecified" : d.WinReason.Trim())
            .Select(g => new WinLossReasonStatDto(
                Reason: g.Key,
                DealCount: g.Count(),
                PercentageOfTotal: wonDeals.Count > 0 ? Math.Round((g.Count() / (decimal)wonDeals.Count) * 100m, 2) : 0m,
                TotalRevenue: g.Sum(d => d.Amount)
            ))
            .OrderByDescending(r => r.DealCount)
            .ToList();

        var lossReasons = lostDeals
            .GroupBy(d => string.IsNullOrWhiteSpace(d.LostReason) ? "Unspecified" : d.LostReason.Trim())
            .Select(g => new WinLossReasonStatDto(
                Reason: g.Key,
                DealCount: g.Count(),
                PercentageOfTotal: lostDeals.Count > 0 ? Math.Round((g.Count() / (decimal)lostDeals.Count) * 100m, 2) : 0m,
                TotalRevenue: g.Sum(d => d.Amount)
            ))
            .OrderByDescending(r => r.DealCount)
            .ToList();

        return new WinLossReasonsBreakdownDto(winReasons, lossReasons);
    }

    private async Task<IReadOnlyList<CompetitorWinLossDto>> ComputeCompetitorCorrelationAsync(List<Deal> deals, CancellationToken cancellationToken)
    {
        if (deals.Count == 0) return [];

        var dealIds = deals.Select(d => d.Id).ToList();

        var associations = await db.DealCompetitors.AsNoTracking()
            .Where(dc => dealIds.Contains(dc.DealId))
            .Include(dc => dc.Competitor)
            .ToListAsync(cancellationToken);

        if (associations.Count == 0) return [];

        var dealsById = deals.ToDictionary(d => d.Id);

        return associations
            .GroupBy(dc => dc.CompetitorId)
            .Select(g =>
            {
                var compName = g.First().Competitor?.Name ?? "Unknown Competitor";
                var compDeals = g.Select(x => dealsById.GetValueOrDefault(x.DealId)).Where(d => d is not null).ToList();

                var won = compDeals.Where(d => string.Equals(d!.Status, "Won", StringComparison.OrdinalIgnoreCase)).ToList();
                var lost = compDeals.Where(d => string.Equals(d!.Status, "Lost", StringComparison.OrdinalIgnoreCase)).ToList();

                var closed = won.Count + lost.Count;
                var winRate = closed > 0 ? Math.Round((won.Count / (decimal)closed) * 100m, 2) : 0m;

                return new CompetitorWinLossDto(
                    CompetitorId: g.Key,
                    CompetitorName: compName,
                    EncountersCount: compDeals.Count,
                    WonCount: won.Count,
                    LostCount: lost.Count,
                    WinRatePercentage: winRate,
                    WonRevenue: won.Sum(d => d!.Amount),
                    LostRevenue: lost.Sum(d => d!.Amount)
                );
            })
            .OrderByDescending(c => c.EncountersCount)
            .ToList();
    }

    private async Task<IReadOnlyList<StageLeakageDto>> ComputePipelineLeakageAsync(List<Deal> deals, CancellationToken cancellationToken)
    {
        var lostDeals = deals.Where(d => string.Equals(d.Status, "Lost", StringComparison.OrdinalIgnoreCase)).ToList();
        if (lostDeals.Count == 0) return [];

        var stageIds = lostDeals.Select(d => d.StageId).Distinct().ToList();
        var stages = await db.PipelineStages.AsNoTracking()
            .Where(s => stageIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var totalLostRevenue = lostDeals.Sum(d => d.Amount);

        return lostDeals
            .GroupBy(d => d.StageId)
            .Select(g =>
            {
                var stage = stages.GetValueOrDefault(g.Key);
                var lostAmt = g.Sum(d => d.Amount);
                var leakagePct = totalLostRevenue > 0
                    ? Math.Round((lostAmt / totalLostRevenue) * 100m, 2)
                    : 0m;

                return new StageLeakageDto(
                    StageId: g.Key,
                    StageName: stage?.Name ?? "Unknown Stage",
                    StageOrder: stage?.SortOrder ?? 0,
                    LostCount: g.Count(),
                    LostRevenue: lostAmt,
                    LeakagePercentage: leakagePct
                );
            })
            .OrderBy(s => s.StageOrder)
            .ToList();
    }

    private async Task<IReadOnlyList<RepWinLossDto>> ComputeSalesRepPerformanceAsync(List<Deal> deals, CancellationToken cancellationToken)
    {
        if (deals.Count == 0) return [];

        var ownerIds = deals.Select(d => d.OwnerId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return deals
            .GroupBy(d => d.OwnerId)
            .Select(g =>
            {
                var user = users.GetValueOrDefault(g.Key);
                var name = user is not null ? $"{user.FirstName} {user.LastName}".Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    name = user?.UserName ?? user?.Email ?? "Unknown User";

                var userDeals = g.ToList();
                var won = userDeals.Where(d => string.Equals(d.Status, "Won", StringComparison.OrdinalIgnoreCase)).ToList();
                var lost = userDeals.Where(d => string.Equals(d.Status, "Lost", StringComparison.OrdinalIgnoreCase)).ToList();

                var total = userDeals.Count;
                var winRate = total > 0 ? Math.Round((won.Count / (decimal)total) * 100m, 2) : 0m;

                var avgDaysWin = won.Count > 0
                    ? Math.Round((decimal)won.Average(d => Math.Max(0, ((d.ClosedAt ?? d.CreatedAt) - d.CreatedAt).TotalDays)), 1)
                    : 0m;

                var avgDaysLose = lost.Count > 0
                    ? Math.Round((decimal)lost.Average(d => Math.Max(0, ((d.ClosedAt ?? d.CreatedAt) - d.CreatedAt).TotalDays)), 1)
                    : 0m;

                return new RepWinLossDto(
                    OwnerId: g.Key,
                    OwnerName: name,
                    TotalDeals: total,
                    WonDeals: won.Count,
                    LostDeals: lost.Count,
                    WinRatePercentage: winRate,
                    WonRevenue: won.Sum(d => d.Amount),
                    LostRevenue: lost.Sum(d => d.Amount),
                    AverageDaysToWin: avgDaysWin,
                    AverageDaysToLose: avgDaysLose
                );
            })
            .OrderByDescending(r => r.WonRevenue)
            .ToList();
    }
}
