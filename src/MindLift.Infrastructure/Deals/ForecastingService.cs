using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Deals;

/// <summary>
/// Phase 3 / #15: Forecasting and Weighted Pipeline (forecasting).
/// Service for calculating weighted pipeline projections, forecast categories, and quota attainments.
/// </summary>
public sealed class ForecastingService(
    AppDbContext db,
    ITenantContext tenant,
    DealService dealService) : IForecastingService
{
    public async Task<ForecastSummaryDto> GetForecastSummaryAsync(ForecastFilterQuery query, CancellationToken cancellationToken = default)
    {
        var (start, end, period) = ResolvePeriod(query);
        var deals = await GetDealsInPeriodAsync(query, start, end, cancellationToken);
        var categoryBreakdown = ComputeCategoryBreakdowns(deals);
        var userAttainments = await ComputeUserAttainmentsAsync(query, deals, start, end, cancellationToken);

        var totalQuota = userAttainments.Sum(u => u.QuotaTarget);
        var closedWonAmount = deals
            .Where(d => ResolveCategory(d) == ForecastCategories.Closed)
            .Sum(d => d.Amount);

        var openDeals = deals
            .Where(d => ResolveCategory(d) is ForecastCategories.Commit or ForecastCategories.BestCase or ForecastCategories.Pipeline)
            .ToList();

        var totalPipelineAmount = openDeals.Sum(d => d.Amount);
        var weightedPipelineAmount = openDeals.Sum(d => ComputeWeightedAmount(d, ResolveCategory(d)));

        var commitAmount = deals
            .Where(d => ResolveCategory(d) == ForecastCategories.Commit)
            .Sum(d => d.Amount);

        var bestCaseAmount = deals
            .Where(d => ResolveCategory(d) == ForecastCategories.BestCase)
            .Sum(d => d.Amount);

        var projectedAttainment = closedWonAmount + weightedPipelineAmount;
        var attainmentPct = totalQuota > 0m
            ? Math.Round((projectedAttainment / totalQuota) * 100m, 2)
            : 0m;
        var gapToTarget = Math.Max(0m, totalQuota - projectedAttainment);

        return new ForecastSummaryDto(
            Period: period,
            StartDate: start,
            EndDate: end,
            TotalQuota: totalQuota,
            ClosedWonAmount: closedWonAmount,
            TotalPipelineAmount: totalPipelineAmount,
            WeightedPipelineAmount: weightedPipelineAmount,
            CommitAmount: commitAmount,
            BestCaseAmount: bestCaseAmount,
            ProjectedAttainmentAmount: projectedAttainment,
            AttainmentPercentage: attainmentPct,
            GapToTarget: gapToTarget,
            ByCategory: categoryBreakdown,
            ByUser: userAttainments
        );
    }

    public async Task<IReadOnlyList<UserForecastAttainmentDto>> GetUserAttainmentsAsync(ForecastFilterQuery query, CancellationToken cancellationToken = default)
    {
        var (start, end, _) = ResolvePeriod(query);
        var deals = await GetDealsInPeriodAsync(query, start, end, cancellationToken);
        return await ComputeUserAttainmentsAsync(query, deals, start, end, cancellationToken);
    }

    public async Task<IReadOnlyList<ForecastCategorySummaryDto>> GetCategoryBreakdownsAsync(ForecastFilterQuery query, CancellationToken cancellationToken = default)
    {
        var (start, end, _) = ResolvePeriod(query);
        var deals = await GetDealsInPeriodAsync(query, start, end, cancellationToken);
        return ComputeCategoryBreakdowns(deals);
    }

    public async Task<DealDto> UpdateDealForecastCategoryAsync(Guid dealId, UpdateDealForecastCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ForecastCategory) || !ForecastCategories.All.Contains(request.ForecastCategory.Trim()))
        {
            throw new ValidationException("ForecastCategory", $"Invalid forecast category '{request.ForecastCategory}'. Valid categories are: {string.Join(", ", ForecastCategories.All)}");
        }

        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId, cancellationToken)
            ?? throw new NotFoundException("Deal", dealId);

        deal.ForecastCategory = request.ForecastCategory.Trim();
        await db.SaveChangesAsync(cancellationToken);

        return await dealService.GetAsync(dealId, cancellationToken);
    }

    private static (DateTime Start, DateTime End, string Period) ResolvePeriod(ForecastFilterQuery query)
    {
        if (query.StartDate.HasValue && query.EndDate.HasValue)
        {
            var s = DateTime.SpecifyKind(query.StartDate.Value, DateTimeKind.Utc);
            var e = DateTime.SpecifyKind(query.EndDate.Value, DateTimeKind.Utc);
            return (s, e, $"{s:yyyy-MM-dd} - {e:yyyy-MM-dd}");
        }

        var now = DateTime.UtcNow;
        var qtr = (now.Month - 1) / 3 + 1;
        var startMonth = (qtr - 1) * 3 + 1;
        var start = query.StartDate.HasValue
            ? DateTime.SpecifyKind(query.StartDate.Value, DateTimeKind.Utc)
            : new DateTime(now.Year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = query.EndDate.HasValue
            ? DateTime.SpecifyKind(query.EndDate.Value, DateTimeKind.Utc)
            : start.AddMonths(3).AddDays(0);

        var period = query.StartDate.HasValue || query.EndDate.HasValue
            ? $"{start:yyyy-MM-dd} - {end:yyyy-MM-dd}"
            : $"Q{qtr} {now.Year}";

        return (start, end, period);
    }

    private async Task<List<Deal>> GetDealsInPeriodAsync(ForecastFilterQuery query, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var dealQuery = db.Deals.AsNoTracking().AsQueryable();

        if (query.PipelineId.HasValue)
            dealQuery = dealQuery.Where(d => d.PipelineId == query.PipelineId.Value);

        if (query.UserId.HasValue)
            dealQuery = dealQuery.Where(d => d.OwnerId == query.UserId.Value);

        var allDeals = await dealQuery.ToListAsync(cancellationToken);

        return allDeals.Where(d =>
        {
            var effectiveDate = d.Status switch
            {
                "Won" or "Lost" => d.ClosedAt ?? d.ExpectedCloseDate ?? d.CreatedAt,
                _ => d.ExpectedCloseDate ?? d.CreatedAt
            };
            return effectiveDate >= start && effectiveDate <= end;
        }).ToList();
    }

    private static string ResolveCategory(Deal deal)
    {
        if (string.Equals(deal.Status, "Won", StringComparison.OrdinalIgnoreCase))
            return ForecastCategories.Closed;

        if (string.Equals(deal.Status, "Lost", StringComparison.OrdinalIgnoreCase))
            return ForecastCategories.Omitted;

        if (deal.ForecastCategory is ForecastCategories.Commit or ForecastCategories.BestCase or ForecastCategories.Omitted or ForecastCategories.Closed)
            return deal.ForecastCategory;

        // Pipeline / default -> auto-derive by probability
        if (deal.Probability >= 80)
            return ForecastCategories.Commit;
        if (deal.Probability >= 50)
            return ForecastCategories.BestCase;

        return ForecastCategories.Pipeline;
    }

    private static decimal ComputeWeightedAmount(Deal deal, string category)
    {
        if (category == ForecastCategories.Closed)
            return deal.Amount;
        if (category == ForecastCategories.Omitted)
            return 0m;

        var prob = Math.Clamp(deal.Probability, 0, 100);
        return Math.Round(deal.Amount * (prob / 100m), 2);
    }

    private static IReadOnlyList<ForecastCategorySummaryDto> ComputeCategoryBreakdowns(List<Deal> deals)
    {
        var categories = new[]
        {
            ForecastCategories.Commit,
            ForecastCategories.BestCase,
            ForecastCategories.Pipeline,
            ForecastCategories.Closed,
            ForecastCategories.Omitted
        };

        var list = new List<ForecastCategorySummaryDto>();

        foreach (var cat in categories)
        {
            var matching = deals.Where(d => ResolveCategory(d) == cat).ToList();
            var count = matching.Count;
            var total = matching.Sum(d => d.Amount);
            var weighted = matching.Sum(d => ComputeWeightedAmount(d, cat));
            list.Add(new ForecastCategorySummaryDto(cat, count, total, weighted));
        }

        return list;
    }

    private async Task<IReadOnlyList<UserForecastAttainmentDto>> ComputeUserAttainmentsAsync(
        ForecastFilterQuery query,
        List<Deal> deals,
        DateTime start,
        DateTime end,
        CancellationToken cancellationToken)
    {
        var startDay = DateOnly.FromDateTime(start);
        var endDay = DateOnly.FromDateTime(end);

        var targetQuery = db.SalesTargets.AsNoTracking()
            .Where(t => t.Metric == SalesTargetMetric.WonAmount && t.StartDate <= endDay && t.EndDate >= startDay);

        if (query.UserId.HasValue)
            targetQuery = targetQuery.Where(t => t.UserId == query.UserId.Value);

        var targets = await targetQuery.ToListAsync(cancellationToken);
        var targetsByUser = targets
            .Where(t => t.UserId.HasValue)
            .GroupBy(t => t.UserId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.TargetValue));

        var userIds = deals.Select(d => d.OwnerId)
            .Concat(targetsByUser.Keys)
            .Distinct()
            .ToList();

        if (query.UserId.HasValue)
            userIds = userIds.Where(id => id == query.UserId.Value).ToList();

        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var result = new List<UserForecastAttainmentDto>();

        foreach (var userId in userIds)
        {
            var user = users.GetValueOrDefault(userId);
            var name = user is not null ? $"{user.FirstName} {user.LastName}".Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                name = user?.UserName ?? user?.Email ?? "Unknown User";

            var email = user?.Email ?? string.Empty;
            var quota = targetsByUser.GetValueOrDefault(userId, 0m);

            var userDeals = deals.Where(d => d.OwnerId == userId).ToList();

            var closedWon = userDeals
                .Where(d => ResolveCategory(d) == ForecastCategories.Closed)
                .Sum(d => d.Amount);

            var commit = userDeals
                .Where(d => ResolveCategory(d) == ForecastCategories.Commit)
                .Sum(d => d.Amount);

            var bestCase = userDeals
                .Where(d => ResolveCategory(d) == ForecastCategories.BestCase)
                .Sum(d => d.Amount);

            var pipeline = userDeals
                .Where(d => ResolveCategory(d) == ForecastCategories.Pipeline)
                .Sum(d => d.Amount);

            var weightedPipeline = userDeals
                .Where(d => ResolveCategory(d) is ForecastCategories.Commit or ForecastCategories.BestCase or ForecastCategories.Pipeline)
                .Sum(d => ComputeWeightedAmount(d, ResolveCategory(d)));

            var projectedTotal = closedWon + weightedPipeline;
            var attainmentPct = quota > 0m
                ? Math.Round((projectedTotal / quota) * 100m, 2)
                : 0m;
            var gap = Math.Max(0m, quota - projectedTotal);

            result.Add(new UserForecastAttainmentDto(
                UserId: userId,
                UserName: name,
                UserEmail: email,
                QuotaTarget: quota,
                ClosedWonAmount: closedWon,
                CommitAmount: commit,
                BestCaseAmount: bestCase,
                PipelineAmount: pipeline,
                WeightedPipelineAmount: weightedPipeline,
                ProjectedTotal: projectedTotal,
                AttainmentPercentage: attainmentPct,
                GapToTarget: gap
            ));
        }

        return result.OrderByDescending(u => u.ProjectedTotal).ToList();
    }
}
