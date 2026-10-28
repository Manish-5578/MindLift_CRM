using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Reporting;

public sealed class VelocityReportService(
    AppDbContext db,
    ITenantContext tenantContext) : IVelocityReportService
{
    public async Task<PipelineFunnelReportDto> GetFunnelReportAsync(PipelineFunnelReportRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        Pipeline? pipeline;
        if (request.PipelineId.HasValue)
        {
            pipeline = await db.Pipelines.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PipelineId.Value, ct);
            if (pipeline == null)
                throw new NotFoundException("pipeline_not_found", "Pipeline was not found.");
        }
        else
        {
            pipeline = await db.Pipelines.AsNoTracking().FirstOrDefaultAsync(p => p.IsDefault, ct)
                       ?? await db.Pipelines.AsNoTracking().FirstOrDefaultAsync(ct);
            if (pipeline == null)
            {
                return new PipelineFunnelReportDto(Guid.Empty, "Default Pipeline", 0, 0, 0, 0m, 0m, 0m, 0m, []);
            }
        }

        var stages = await db.PipelineStages.AsNoTracking()
            .Where(s => s.PipelineId == pipeline.Id)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        var dealQuery = db.Deals.AsNoTracking().Where(d => d.PipelineId == pipeline.Id);

        if (request.FromUtc.HasValue)
            dealQuery = dealQuery.Where(d => d.CreatedAt >= request.FromUtc.Value);

        if (request.ToUtc.HasValue)
            dealQuery = dealQuery.Where(d => d.CreatedAt <= request.ToUtc.Value);

        if (request.OwnerId.HasValue)
            dealQuery = dealQuery.Where(d => d.OwnerId == request.OwnerId.Value);

        var deals = await dealQuery.ToListAsync(ct);

        var totalDeals = deals.Count;
        var wonDeals = deals.Count(d => d.Status.Equals("Won", StringComparison.OrdinalIgnoreCase));
        var lostDeals = deals.Count(d => d.Status.Equals("Lost", StringComparison.OrdinalIgnoreCase));
        var overallWinRate = totalDeals > 0 ? Math.Round((decimal)wonDeals / totalDeals * 100m, 1) : 0m;
        var totalPipelineVal = deals.Sum(d => d.Amount);
        var wonVal = deals.Where(d => d.Status.Equals("Won", StringComparison.OrdinalIgnoreCase)).Sum(d => d.Amount);

        var stageMetrics = new List<FunnelStageMetricDto>();
        var now = DateTime.UtcNow;

        for (var i = 0; i < stages.Count; i++)
        {
            var s = stages[i];
            var currentInStage = deals.Where(d => d.StageId == s.Id).ToList();
            var dealsInOrPastStage = deals.Where(d =>
            {
                var dealStage = stages.FirstOrDefault(st => st.Id == d.StageId);
                return dealStage != null && dealStage.SortOrder >= s.SortOrder;
            }).ToList();

            var count = currentInStage.Count;
            var val = currentInStage.Sum(d => d.Amount);
            var dropped = currentInStage.Count(d => d.Status.Equals("Lost", StringComparison.OrdinalIgnoreCase));

            // Conversion rate to next stage
            decimal conversionRate = 0m;
            if (i < stages.Count - 1)
            {
                var nextStage = stages[i + 1];
                var inNextOrPast = deals.Count(d =>
                {
                    var dealStage = stages.FirstOrDefault(st => st.Id == d.StageId);
                    return dealStage != null && dealStage.SortOrder >= nextStage.SortOrder;
                });

                if (dealsInOrPastStage.Count > 0)
                {
                    conversionRate = Math.Round((decimal)inNextOrPast / dealsInOrPastStage.Count * 100m, 1);
                }
            }
            else
            {
                conversionRate = 100m;
            }

            // Duration in days
            double avgDuration = 3.5;
            if (currentInStage.Count > 0)
            {
                avgDuration = Math.Round(currentInStage.Average(d => (now - (d.StageChangedAt == default ? d.CreatedAt : d.StageChangedAt)).TotalDays), 1);
                if (avgDuration < 0.5) avgDuration = 1.0;
            }

            stageMetrics.Add(new FunnelStageMetricDto(
                StageId: s.Id,
                StageName: s.Name,
                SortOrder: s.SortOrder,
                StageType: s.StageType,
                DealsCount: count,
                DealsValue: val,
                ConversionRateToNext: conversionRate,
                AverageDurationDays: avgDuration,
                DroppedCount: dropped));
        }

        return new PipelineFunnelReportDto(
            PipelineId: pipeline.Id,
            PipelineName: pipeline.Name,
            TotalDealsEntered: totalDeals,
            TotalDealsWon: wonDeals,
            TotalDealsLost: lostDeals,
            OverallWinRate: overallWinRate,
            OverallConversionRate: overallWinRate,
            TotalPipelineValue: totalPipelineVal,
            WonValue: wonVal,
            Stages: stageMetrics);
    }

    public async Task<SalesVelocityReportDto> GetSalesVelocityAsync(SalesVelocityReportRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var baseCurrency = settings?.BaseCurrency ?? "USD";

        var query = db.Deals.AsNoTracking();

        if (request.PipelineId.HasValue)
            query = query.Where(d => d.PipelineId == request.PipelineId.Value);

        if (request.FromUtc.HasValue)
            query = query.Where(d => d.CreatedAt >= request.FromUtc.Value);

        if (request.ToUtc.HasValue)
            query = query.Where(d => d.CreatedAt <= request.ToUtc.Value);

        if (request.OwnerId.HasValue)
            query = query.Where(d => d.OwnerId == request.OwnerId.Value);

        var deals = await query.ToListAsync(ct);

        if (deals.Count == 0)
        {
            return new SalesVelocityReportDto(
                SalesVelocityPerDay: 0m,
                NumberOfOpportunities: 0,
                WinRatePercentage: 0m,
                AverageDealSize: 0m,
                SalesCycleLengthDays: 0,
                Currency: baseCurrency,
                RepVelocities: []);
        }

        var wonDeals = deals.Where(d => d.Status.Equals("Won", StringComparison.OrdinalIgnoreCase)).ToList();
        var numOpportunities = deals.Count;
        var winRateFraction = (decimal)wonDeals.Count / numOpportunities;
        var winRatePercent = Math.Round(winRateFraction * 100m, 1);

        var avgDealSize = wonDeals.Count > 0
            ? Math.Round((decimal)wonDeals.Average(d => d.Amount), 2)
            : Math.Round((decimal)deals.Average(d => d.Amount), 2);

        // Sales cycle length in days
        double cycleDays = 14.0;
        if (wonDeals.Count > 0)
        {
            cycleDays = Math.Round(wonDeals.Average(d => Math.Max(1.0, ((d.StageChangedAt == default ? DateTime.UtcNow : d.StageChangedAt) - d.CreatedAt).TotalDays)), 1);
        }
        else
        {
            cycleDays = Math.Round(deals.Average(d => Math.Max(1.0, (DateTime.UtcNow - d.CreatedAt).TotalDays)), 1);
        }

        if (cycleDays < 1.0) cycleDays = 1.0;

        // Sales Velocity Equation: (Opps * WinRate * AvgDealSize) / CycleLength
        var velocityPerDay = Math.Round((numOpportunities * winRateFraction * avgDealSize) / (decimal)cycleDays, 2);

        // Rep velocities
        var ownerIds = deals.Select(d => d.OwnerId).Distinct().ToList();
        var owners = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var repVelocities = new List<OwnerVelocityDto>();
        foreach (var ownerId in ownerIds)
        {
            var repDeals = deals.Where(d => d.OwnerId == ownerId).ToList();
            var repWon = repDeals.Where(d => d.Status.Equals("Won", StringComparison.OrdinalIgnoreCase)).ToList();
            var repWinFraction = repDeals.Count > 0 ? (decimal)repWon.Count / repDeals.Count : 0m;
            var repAvgSize = repWon.Count > 0 ? Math.Round((decimal)repWon.Average(d => d.Amount), 2) : (repDeals.Count > 0 ? Math.Round((decimal)repDeals.Average(d => d.Amount), 2) : 0m);
            var repCycleDays = repWon.Count > 0
                ? Math.Round(repWon.Average(d => Math.Max(1.0, ((d.StageChangedAt == default ? DateTime.UtcNow : d.StageChangedAt) - d.CreatedAt).TotalDays)), 1)
                : 14.0;
            if (repCycleDays < 1.0) repCycleDays = 1.0;

            var repVelocity = Math.Round((repDeals.Count * repWinFraction * repAvgSize) / (decimal)repCycleDays, 2);

            repVelocities.Add(new OwnerVelocityDto(
                OwnerId: ownerId,
                OwnerName: owners.GetValueOrDefault(ownerId, "Sales Rep"),
                SalesVelocityPerDay: repVelocity,
                OpportunitiesCount: repDeals.Count,
                WinRatePercentage: Math.Round(repWinFraction * 100m, 1),
                AverageDealSize: repAvgSize,
                AverageCycleDays: repCycleDays));
        }

        return new SalesVelocityReportDto(
            SalesVelocityPerDay: velocityPerDay,
            NumberOfOpportunities: numOpportunities,
            WinRatePercentage: winRatePercent,
            AverageDealSize: avgDealSize,
            SalesCycleLengthDays: cycleDays,
            Currency: baseCurrency,
            RepVelocities: repVelocities.OrderByDescending(r => r.SalesVelocityPerDay).ToList());
    }
}
