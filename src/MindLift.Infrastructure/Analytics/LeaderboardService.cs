using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Analytics;

public sealed class LeaderboardService(
    AppDbContext db,
    ITenantContext tenantContext) : ILeaderboardService
{
    private static readonly string[] ValidMetrics = ["revenue", "deals_won", "target_attainment", "activities", "pipeline_created"];
    private static readonly string[] ValidPeriods = ["this_week", "this_month", "this_quarter", "this_year"];

    public Task<LeaderboardMetaDto> GetMetaAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new LeaderboardMetaDto(ValidMetrics, ValidPeriods));
    }

    public async Task<LeaderboardResponseDto> GetLeaderboardAsync(LeaderboardQueryRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var metric = string.IsNullOrWhiteSpace(request.Metric) ? "revenue" : request.Metric.Trim().ToLowerInvariant();
        var period = string.IsNullOrWhiteSpace(request.Period) ? "this_month" : request.Period.Trim().ToLowerInvariant();

        var (startUtc, endUtc) = ResolvePeriodDates(period, request.FromUtc, request.ToUtc);

        var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var currency = settings?.BaseCurrency ?? "USD";

        var userQuery = db.Users.AsNoTracking();
        if (request.TeamId.HasValue)
        {
            userQuery = userQuery.Where(u => u.TeamId == request.TeamId.Value);
        }

        var users = await userQuery.ToListAsync(ct);
        var userIds = users.Select(u => u.Id).ToHashSet();

        // Retrieve deals within period
        var deals = await db.Deals.AsNoTracking()
            .Where(d => userIds.Contains(d.OwnerId) && d.CreatedAt >= startUtc && d.CreatedAt <= endUtc)
            .ToListAsync(ct);

        // Retrieve activities within period
        var activities = await db.Activities.AsNoTracking()
            .Where(a => userIds.Contains(a.OwnerId) && a.CreatedAt >= startUtc && a.CreatedAt <= endUtc)
            .ToListAsync(ct);

        // Retrieve sales targets
        var targets = await db.SalesTargets.AsNoTracking()
            .Where(t => t.UserId.HasValue && userIds.Contains(t.UserId.Value))
            .ToListAsync(ct);

        var rawEntries = new List<(Guid UserId, string Name, string? Email, decimal Score, decimal? Target, decimal? Attainment)>();

        foreach (var u in users)
        {
            var uDeals = deals.Where(d => d.OwnerId == u.Id).ToList();
            var uWon = uDeals.Where(d => d.Status.Equals("Won", StringComparison.OrdinalIgnoreCase)).ToList();
            var uActivities = activities.Where(a => a.OwnerId == u.Id).ToList();
            var uTarget = targets.FirstOrDefault(t => t.UserId == u.Id);

            decimal score;
            decimal? targetVal = uTarget?.TargetValue;
            decimal? attainment = null;

            switch (metric)
            {
                case "deals_won":
                    score = uWon.Count;
                    break;
                case "activities":
                    score = uActivities.Count;
                    break;
                case "pipeline_created":
                    score = uDeals.Sum(d => d.Amount);
                    break;
                case "target_attainment":
                    var targetAmount = targetVal ?? 100000m;
                    targetVal = targetAmount;
                    var wonAmount = uWon.Sum(d => d.Amount);
                    attainment = targetAmount > 0 ? Math.Round(wonAmount / targetAmount * 100m, 1) : 0m;
                    score = attainment.Value;
                    break;
                case "revenue":
                default:
                    score = uWon.Sum(d => d.Amount);
                    break;
            }

            var name = $"{u.FirstName} {u.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(name)) name = u.Email;

            rawEntries.Add((u.Id, name, u.Email, score, targetVal, attainment));
        }

        // Rank entries
        var sorted = rawEntries.OrderByDescending(e => e.Score).ThenBy(e => e.Name).ToList();
        var entries = new List<LeaderboardEntryDto>();

        for (var i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i];
            var rank = i + 1;
            var (badge, tier) = ResolveBadgeAndTier(rank, item.Score);
            var formatted = FormatScore(metric, item.Score, currency);

            entries.Add(new LeaderboardEntryDto(
                Rank: rank,
                UserId: item.UserId,
                UserName: item.Name,
                UserEmail: item.Email,
                Score: item.Score,
                FormattedScore: formatted,
                TargetAmount: item.Target,
                AttainmentPercentage: item.Attainment,
                Badge: badge,
                Tier: tier,
                RankChange: 0));
        }

        var podium = entries.Take(3).ToList();

        // Calculate caller's rank
        CurrentUserRankDto? myRank = null;
        var callerUserId = tenantContext.UserId;
        if (callerUserId.HasValue)
        {
            var myEntry = entries.FirstOrDefault(e => e.UserId == callerUserId.Value);
            if (myEntry != null)
            {
                decimal? gap = null;
                string? nextUser = null;
                if (myEntry.Rank > 1)
                {
                    var ahead = entries[myEntry.Rank - 2];
                    gap = ahead.Score - myEntry.Score;
                    nextUser = ahead.UserName;
                }

                myRank = new CurrentUserRankDto(
                    Rank: myEntry.Rank,
                    Score: myEntry.Score,
                    FormattedScore: myEntry.FormattedScore,
                    GapToNextRank: gap,
                    NextRankUserName: nextUser);
            }
        }

        return new LeaderboardResponseDto(
            Metric: metric,
            Period: period,
            PeriodStartUtc: startUtc,
            PeriodEndUtc: endUtc,
            Currency: currency,
            Podium: podium,
            Entries: entries,
            MyRank: myRank);
    }

    private static (DateTime startUtc, DateTime endUtc) ResolvePeriodDates(string period, DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc.HasValue && toUtc.HasValue)
            return (fromUtc.Value, toUtc.Value);

        var now = DateTime.UtcNow;
        return period switch
        {
            "this_week" => (now.AddDays(-(int)now.DayOfWeek), now.AddDays(7 - (int)now.DayOfWeek)),
            "this_quarter" => (new DateTime(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc), now.AddMonths(3)),
            "this_year" => (new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(now.Year, 12, 31, 23, 59, 59, DateTimeKind.Utc)),
            _ => (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month), 23, 59, 59, DateTimeKind.Utc))
        };
    }

    private static (string Badge, string Tier) ResolveBadgeAndTier(int rank, decimal score)
    {
        return rank switch
        {
            1 => ("Champion 🏆", "Diamond"),
            2 => ("Runner-up 🥈", "Gold"),
            3 => ("Podium 🥉", "Silver"),
            _ => score > 0 ? ("Contender", "Bronze") : ("Starter", "Base")
        };
    }

    private static string FormatScore(string metric, decimal score, string currency)
    {
        return metric switch
        {
            "deals_won" => FormattableString.Invariant($"{score:N0} deals"),
            "activities" => FormattableString.Invariant($"{score:N0} activities"),
            "target_attainment" => FormattableString.Invariant($"{score:N1}%"),
            _ => FormattableString.Invariant($"{currency} {score:N0}")
        };
    }
}
