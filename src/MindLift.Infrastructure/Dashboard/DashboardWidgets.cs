using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Dashboard;

public sealed class KpiStripWidget(ISalesMetrics metrics, AppDbContext db) : IDashboardWidget
{
    public string Key => "kpi_strip";
    public AppModule Module => AppModule.Dashboard;
    public DashboardView[] Views => [DashboardView.Staff, DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Xl;
    public int DefaultOrder => 10;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var tiles = new List<KpiTileDto>();

        // 1. Won Amount
        var wonNow = await metrics.WonAmountAsync(ctx.ScopeUserIds, ctx.Period, ctx.PipelineId, ct);
        var wonPrev = await metrics.WonAmountAsync(ctx.ScopeUserIds, ctx.PreviousPeriod, ctx.PipelineId, ct);
        var wonChange = ChangePct(wonNow, wonPrev);
        tiles.Add(new KpiTileDto("won_amount", "Sales", wonNow, wonPrev, wonChange, "currency", Trend(wonChange), "up", "/deals?view=board"));

        // 2. Open Pipeline
        var (pipeTotal, pipeWeighted, _) = await metrics.OpenPipelineAsync(ctx.ScopeUserIds, ctx.PipelineId, ct);
        tiles.Add(new KpiTileDto("open_pipeline", "Open Pipeline", pipeTotal, null, null, "currency", "flat", "up", "/deals?view=board"));

        // 3. New Leads
        var leadsNow = await metrics.NewLeadsAsync(ctx.ScopeUserIds, ctx.Period, ct);
        var leadsPrev = await metrics.NewLeadsAsync(ctx.ScopeUserIds, ctx.PreviousPeriod, ct);
        var leadsChange = ChangePct(leadsNow, leadsPrev);
        tiles.Add(new KpiTileDto("new_leads", "New Leads", leadsNow, leadsPrev, leadsChange, "number", Trend(leadsChange), "up", "/leads"));

        // 4. Win Rate
        var wonCount = await metrics.WonCountAsync(ctx.ScopeUserIds, ctx.Period, ctx.PipelineId, ct);
        var lostCount = await metrics.LostCountAsync(ctx.ScopeUserIds, ctx.Period, ctx.PipelineId, ct);
        var totalClosed = wonCount + lostCount;
        var winRate = totalClosed == 0 ? 0m : Math.Round(wonCount * 100m / totalClosed, 1);

        var wonCountPrev = await metrics.WonCountAsync(ctx.ScopeUserIds, ctx.PreviousPeriod, ctx.PipelineId, ct);
        var lostCountPrev = await metrics.LostCountAsync(ctx.ScopeUserIds, ctx.PreviousPeriod, ctx.PipelineId, ct);
        var totalClosedPrev = wonCountPrev + lostCountPrev;
        var winRatePrev = totalClosedPrev == 0 ? 0m : Math.Round(wonCountPrev * 100m / totalClosedPrev, 1);
        var winRateChange = ChangePct(winRate, winRatePrev);
        tiles.Add(new KpiTileDto("win_rate", "Win Rate", winRate, winRatePrev, winRateChange, "percent", Trend(winRateChange), "up", "/deals"));

        if (ctx.View != DashboardView.Staff)
        {
            // 5. Conversion Rate
            var createdInCohort = await metrics.NewLeadsAsync(ctx.ScopeUserIds, ctx.Period, ct);
            var convertedInCohort = await metrics.ConvertedLeadsCohortAsync(ctx.ScopeUserIds, ctx.Period, ct);
            var convRate = createdInCohort == 0 ? 0m : Math.Round(convertedInCohort * 100m / createdInCohort, 1);
            tiles.Add(new KpiTileDto("conversion_rate", "Lead Conversion", convRate, null, null, "percent", "flat", "up", "/leads"));

            // 6. Avg Deal Size
            var avgSize = wonCount == 0 ? 0m : Math.Round(wonNow / wonCount, 2);
            tiles.Add(new KpiTileDto("avg_deal_size", "Avg Deal Size", avgSize, null, null, "currency", "flat", "up", "/deals"));
        }

        // 7. Overdue Tasks
        var overdue = await metrics.OverdueTasksAsync(ctx.ScopeUserIds, ct);
        tiles.Add(new KpiTileDto("tasks_overdue", "Tasks Overdue", overdue, null, null, "number", "flat", "down", "/activities?view=overdue"));

        if (ctx.View == DashboardView.Staff)
        {
            var activitiesDone = await metrics.ActivitiesCompletedAsync(ctx.ScopeUserIds, ctx.Period, null, ct);
            tiles.Add(new KpiTileDto("activities_done", "Completed Activities", activitiesDone, null, null, "number", "flat", "up", "/activities?view=completed"));
        }

        return new WidgetResult("kpi_strip", "Key Performance Indicators", WidgetSize.Xl, tiles, null);
    }

    private static decimal? ChangePct(decimal current, decimal previous)
        => previous == 0m ? null : Math.Round((current - previous) / previous * 100m, 1);

    private static string Trend(decimal? change)
        => change is null or 0m ? "flat" : change > 0m ? "up" : "down";
}

public sealed class MyDayWidget(AppDbContext db, IClock clock) : IDashboardWidget
{
    public string Key => "my_day";
    public AppModule Module => AppModule.Activities;
    public DashboardView[] Views => [DashboardView.Staff, DashboardView.Manager];
    public WidgetSize DefaultSize => WidgetSize.Lg;
    public int DefaultOrder => 20;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(now, ctx.TimeZone).Date;
        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(todayLocal, DateTimeKind.Unspecified), ctx.TimeZone);
        var todayEndUtc = todayStartUtc.AddDays(1);

        var tasks = await db.Activities.AsNoTracking()
            .Where(a => a.Type == "Task" && a.Status == "Open" && a.OwnerId == ctx.UserId)
            .OrderBy(a => a.DueAt)
            .ToListAsync(ct);

        var todayTasks = tasks.Where(t => t.DueAt != null && t.DueAt >= todayStartUtc && t.DueAt < todayEndUtc).ToList();
        var overdueTasks = tasks.Where(t => t.DueAt != null && t.DueAt < now).ToList();

        var meetings = await db.Activities.AsNoTracking()
            .Where(a => a.Type == "Meeting" && a.Status == "Open" && a.OwnerId == ctx.UserId && a.DueAt != null && a.DueAt >= todayStartUtc && a.DueAt < todayEndUtc)
            .OrderBy(a => a.DueAt)
            .ToListAsync(ct);

        var todayTaskItems = todayTasks.Take(10).Select(t => new DashboardTaskItem(t.Id, t.Subject, t.DueAt, t.Priority, null, $"/activities", false)).ToList();
        var overdueTaskItems = overdueTasks.Take(10).Select(t => new DashboardTaskItem(t.Id, t.Subject, t.DueAt, t.Priority, null, $"/activities", false)).ToList();
        var meetingItems = meetings.Take(10).Select(m => new DashboardMeetingItem(m.Id, m.Subject, m.DueAt!.Value, m.DueAt.Value.AddHours(1), null, null, null)).ToList();

        var data = new
        {
            TodayTasks = todayTaskItems,
            OverdueTasks = overdueTaskItems,
            TodayMeetings = meetingItems,
            TodayTasksCount = todayTasks.Count,
            OverdueTasksCount = overdueTasks.Count,
            TodayMeetingsCount = meetings.Count
        };

        return new WidgetResult("my_day", "My Day", WidgetSize.Lg, data, "/activities");
    }
}

public sealed class UpcomingMeetingsWidget(AppDbContext db, IClock clock) : IDashboardWidget
{
    public string Key => "upcoming_meetings";
    public AppModule Module => AppModule.Calendar;
    public DashboardView[] Views => [DashboardView.Staff, DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Md;
    public int DefaultOrder => 30;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var end = now.AddDays(7);

        var q = db.Activities.AsNoTracking()
            .Where(a => a.Type == "Meeting" && a.Status == "Open" && a.DueAt != null && a.DueAt >= now && a.DueAt <= end);

        if (ctx.ScopeUserIds is not null)
        {
            q = q.Where(a => ctx.ScopeUserIds.Contains(a.OwnerId));
        }

        var meetings = await q.OrderBy(a => a.DueAt).Take(10).ToListAsync(ct);
        var items = meetings.Select(m => new DashboardMeetingItem(m.Id, m.Subject, m.DueAt!.Value, m.DueAt.Value.AddHours(1), null, null, null)).ToList();

        return new WidgetResult("upcoming_meetings", "Upcoming Meetings", WidgetSize.Md, items, "/calendar");
    }
}

public sealed class NewLeadsWidget(AppDbContext db, IClock clock) : IDashboardWidget
{
    public string Key => "new_leads";
    public AppModule Module => AppModule.Leads;
    public DashboardView[] Views => [DashboardView.Staff, DashboardView.Manager];
    public WidgetSize DefaultSize => WidgetSize.Md;
    public int DefaultOrder => 40;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var q = db.Leads.AsNoTracking().Where(l => l.Status == "new" && l.ConvertedAt == null);
        if (ctx.ScopeUserIds is not null)
        {
            q = q.Where(l => ctx.ScopeUserIds.Contains(l.OwnerId));
        }

        var total = await q.CountAsync(ct);
        var now = clock.UtcNow;
        var top = await q.OrderByDescending(l => l.CreatedAt).Take(10).ToListAsync(ct);

        var items = top.Select(l =>
        {
            var hours = (now - l.CreatedAt).TotalHours;
            var age = hours < 1 ? "Just now" : hours < 24 ? $"{(int)hours}h ago" : $"{(int)(hours / 24)}d ago";
            return new DashboardLeadItem(l.Id, $"{l.FirstName} {l.LastName}".Trim(), l.CompanyName, l.Status, l.Rating, age, hours > 24);
        }).ToList();

        return new WidgetResult("new_leads", "New Leads", WidgetSize.Md, new { Leads = items, TotalCount = total }, "/leads");
    }
}

public sealed class DealsInProgressWidget(AppDbContext db, IClock clock) : IDashboardWidget
{
    public string Key => "deals_in_progress";
    public AppModule Module => AppModule.Deals;
    public DashboardView[] Views => [DashboardView.Staff, DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Md;
    public int DefaultOrder => 50;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var q = db.Deals.AsNoTracking().Where(d => d.Status == "Open");
        if (ctx.ScopeUserIds is not null) q = q.Where(d => ctx.ScopeUserIds.Contains(d.OwnerId));
        if (ctx.PipelineId is not null) q = q.Where(d => d.PipelineId == ctx.PipelineId);

        var deals = await q.ToListAsync(ct);
        var stages = await db.PipelineStages.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var now = clock.UtcNow;

        var byStage = stages.Select(s =>
        {
            var inStage = deals.Where(d => d.StageId == s.Id).ToList();
            var total = inStage.Sum(d => d.Amount);
            var weighted = inStage.Sum(d => d.Amount * d.Probability / 100m);
            return new StageBreakdownItem(s.Id, s.Name, s.Color ?? "#4caf50", inStage.Count, total, weighted);
        }).Where(s => s.Count > 0 || stages.Count <= 8).ToList();

        var rottingCount = deals.Count(d =>
        {
            var stage = stages.FirstOrDefault(s => s.Id == d.StageId);
            return stage?.RottingDays is { } days && (d.LastActivityAt ?? d.StageChangedAt) < now.AddDays(-days);
        });

        var totalInBase = deals.Sum(d => d.Amount);
        var weightedInBase = deals.Sum(d => d.Amount * d.Probability / 100m);

        var data = new DealsInProgressData(deals.Count, totalInBase, weightedInBase, rottingCount, byStage);
        return new WidgetResult("deals_in_progress", "Deals In Progress", WidgetSize.Md, data, "/deals");
    }
}

public sealed class DealsClosingSoonWidget(AppDbContext db, IClock clock) : IDashboardWidget
{
    public string Key => "deals_closing_soon";
    public AppModule Module => AppModule.Deals;
    public DashboardView[] Views => [DashboardView.Staff, DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Md;
    public int DefaultOrder => 60;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var monthEnd = now.AddDays(30);

        var q = db.Deals.AsNoTracking().Where(d => d.Status == "Open" && d.ExpectedCloseDate != null && d.ExpectedCloseDate <= monthEnd);
        if (ctx.ScopeUserIds is not null) q = q.Where(d => ctx.ScopeUserIds.Contains(d.OwnerId));

        var stages = await db.PipelineStages.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var deals = await q.OrderBy(d => d.ExpectedCloseDate).Take(10).ToListAsync(ct);

        var items = deals.Select(d => new ClosingDealItem(
            d.Id,
            d.Name,
            d.Amount,
            d.Currency,
            d.Amount,
            d.ExpectedCloseDate!.Value,
            stages.GetValueOrDefault(d.StageId, "Open"),
            null,
            d.ExpectedCloseDate.Value < now.Date
        )).ToList();

        return new WidgetResult("deals_closing_soon", "Deals Closing Soon", WidgetSize.Md, items, "/deals");
    }
}

public sealed class TargetProgressWidget(AppDbContext db, ISalesMetrics metrics, IClock clock) : IDashboardWidget
{
    public string Key => "target_progress";
    public AppModule Module => AppModule.Reports;
    public DashboardView[] Views => [DashboardView.Staff, DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Sm;
    public int DefaultOrder => 70;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var q = db.SalesTargets.AsNoTracking().Where(t => t.StartDate <= today && t.EndDate >= today);
        if (ctx.View == DashboardView.Staff)
        {
            q = q.Where(t => t.UserId == ctx.UserId);
        }

        var targets = await q.ToListAsync(ct);
        var items = new List<TargetProgressItem>();

        var totalDays = Math.Max(1, (today.ToDateTime(TimeOnly.MinValue) - today.AddMonths(-1).ToDateTime(TimeOnly.MinValue)).Days);
        var elapsedDays = Math.Min(totalDays, Math.Max(1, today.Day));
        var expectedPct = Math.Round((decimal)elapsedDays / totalDays * 100m, 1);

        foreach (var t in targets)
        {
            decimal actual = 0m;
            switch (t.Metric)
            {
                case SalesTargetMetric.WonAmount:
                    actual = await metrics.WonAmountAsync(ctx.ScopeUserIds, ctx.Period, null, ct);
                    break;
                case SalesTargetMetric.WonDeals:
                    actual = await metrics.WonCountAsync(ctx.ScopeUserIds, ctx.Period, null, ct);
                    break;
                case SalesTargetMetric.NewLeads:
                    actual = await metrics.NewLeadsAsync(ctx.ScopeUserIds, ctx.Period, ct);
                    break;
                case SalesTargetMetric.CompletedActivities:
                    actual = await metrics.ActivitiesCompletedAsync(ctx.ScopeUserIds, ctx.Period, null, ct);
                    break;
            }

            var pct = t.TargetValue == 0m ? 0m : Math.Round(actual / t.TargetValue * 100m, 1);
            var status = pct >= expectedPct ? "ahead" : pct >= expectedPct * 0.9m ? "on_track" : "behind";
            items.Add(new TargetProgressItem(t.Metric.ToString(), t.TargetValue, actual, pct, expectedPct, status));
        }

        if (items.Count == 0)
        {
            // Default won revenue target comparison if no explicit target set
            var won = await metrics.WonAmountAsync(ctx.ScopeUserIds, ctx.Period, null, ct);
            items.Add(new TargetProgressItem("Monthly Revenue", 50000m, won, Math.Round(won / 50000m * 100m, 1), expectedPct, won >= 50000m * (expectedPct / 100m) ? "on_track" : "behind"));
        }

        return new WidgetResult("target_progress", "Sales Target Progress", WidgetSize.Sm, items, "/reports");
    }
}

public sealed class SalesTrendWidget(AppDbContext db, IClock clock) : IDashboardWidget
{
    public string Key => "sales_trend";
    public AppModule Module => AppModule.Reports;
    public DashboardView[] Views => [DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Lg;
    public int DefaultOrder => 80;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11);

        var deals = await db.Deals.AsNoTracking()
            .Where(d => d.Status == "Won" && d.ClosedAt != null && d.ClosedAt >= start)
            .Select(d => new { d.Amount, d.ClosedAt })
            .ToListAsync(ct);

        var list = new List<MonthlySalesItem>();
        for (var i = 0; i < 12; i++)
        {
            var m = start.AddMonths(i);
            var nextM = m.AddMonths(1);
            var monthLabel = m.ToString("MMM yyyy", CultureInfo.InvariantCulture);
            var sum = deals.Where(d => d.ClosedAt >= m && d.ClosedAt < nextM).Sum(d => d.Amount);
            list.Add(new MonthlySalesItem(monthLabel, sum, null));
        }

        return new WidgetResult("sales_trend", "12-Month Sales Trend", WidgetSize.Lg, list, "/reports");
    }
}

public sealed class LeadsBySourceWidget(AppDbContext db) : IDashboardWidget
{
    public string Key => "leads_by_source";
    public AppModule Module => AppModule.Leads;
    public DashboardView[] Views => [DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Md;
    public int DefaultOrder => 90;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var q = db.Leads.AsNoTracking().Where(l => l.CreatedAt >= ctx.Period.StartUtc && l.CreatedAt < ctx.Period.EndUtc);
        if (ctx.ScopeUserIds is not null) q = q.Where(l => ctx.ScopeUserIds.Contains(l.OwnerId));

        var leads = await q.Select(l => new { l.Source, l.ConvertedAt }).ToListAsync(ct);
        var groups = leads.GroupBy(l => string.IsNullOrWhiteSpace(l.Source) ? "Unknown" : l.Source)
            .Select(g => new LeadSourceItem(g.Key, g.Count(), g.Count(x => x.ConvertedAt != null)))
            .OrderByDescending(x => x.Count)
            .ToList();

        return new WidgetResult("leads_by_source", "Leads By Source", WidgetSize.Md, groups, "/leads");
    }
}

public sealed class PipelineFunnelWidget(AppDbContext db, IClock clock) : IDashboardWidget
{
    public string Key => "pipeline_funnel";
    public AppModule Module => AppModule.Deals;
    public DashboardView[] Views => [DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Md;
    public int DefaultOrder => 100;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var start = now.AddDays(-90);

        var stages = await db.PipelineStages.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var deals = await db.Deals.AsNoTracking().Where(d => d.CreatedAt >= start).ToListAsync(ct);

        var funnel = stages.Select(s =>
        {
            var inOrPassed = deals.Count(d => d.StageId == s.Id || d.Status == "Won");
            var total = deals.Where(d => d.StageId == s.Id).Sum(d => d.Amount);
            var rate = deals.Count == 0 ? 0m : Math.Round((decimal)inOrPassed / deals.Count * 100m, 1);
            return new FunnelStageItem(s.Id, s.Name, inOrPassed, total, rate);
        }).ToList();

        return new WidgetResult("pipeline_funnel", "Pipeline Funnel Velocity", WidgetSize.Md, funnel, "/deals");
    }
}

public sealed class TeamLeaderboardWidget(AppDbContext db) : IDashboardWidget
{
    public string Key => "team_leaderboard";
    public AppModule Module => AppModule.Reports;
    public DashboardView[] Views => [DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Lg;
    public int DefaultOrder => 110;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Where(u => u.IsActive).ToListAsync(ct);
        if (ctx.ScopeUserIds is not null)
        {
            users = users.Where(u => ctx.ScopeUserIds.Contains(u.Id)).ToList();
        }

        var wonDeals = await db.Deals.AsNoTracking()
            .Where(d => d.Status == "Won" && d.ClosedAt >= ctx.Period.StartUtc && d.ClosedAt < ctx.Period.EndUtc)
            .ToListAsync(ct);

        var openDeals = await db.Deals.AsNoTracking()
            .Where(d => d.Status == "Open")
            .ToListAsync(ct);

        var newLeads = await db.Leads.AsNoTracking()
            .Where(l => l.CreatedAt >= ctx.Period.StartUtc && l.CreatedAt < ctx.Period.EndUtc)
            .ToListAsync(ct);

        var activities = await db.Activities.AsNoTracking()
            .Where(a => a.Status == "Completed" && a.CompletedAt >= ctx.Period.StartUtc && a.CompletedAt < ctx.Period.EndUtc)
            .ToListAsync(ct);

        var list = users.Select(u =>
        {
            var uWon = wonDeals.Where(d => d.OwnerId == u.Id).ToList();
            var uOpen = openDeals.Where(d => d.OwnerId == u.Id).ToList();
            var uLeads = newLeads.Where(l => l.OwnerId == u.Id).Count();
            var uActs = activities.Where(a => a.OwnerId == u.Id).Count();

            var wonAmt = uWon.Sum(d => d.Amount);
            var weighted = uOpen.Sum(d => d.Amount * d.Probability / 100m);
            var targetPct = 100m; // Default quota achieved

            return new LeaderboardUserItem(u.Id, $"{u.FirstName} {u.LastName}".Trim(), wonAmt, uWon.Count, uLeads, uActs, weighted, targetPct);
        }).OrderByDescending(x => x.WonAmount).Take(10).ToList();

        return new WidgetResult("team_leaderboard", "Team Leaderboard", WidgetSize.Lg, list, "/reports");
    }
}

public sealed class TeamOverdueWidget(AppDbContext db, IClock clock) : IDashboardWidget
{
    public string Key => "team_overdue";
    public AppModule Module => AppModule.Activities;
    public DashboardView[] Views => [DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Sm;
    public int DefaultOrder => 120;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var q = db.Activities.AsNoTracking()
            .Where(a => a.Type == "Task" && a.Status == "Open" && a.DueAt != null && a.DueAt < now);

        if (ctx.ScopeUserIds is not null)
        {
            q = q.Where(a => ctx.ScopeUserIds.Contains(a.OwnerId));
        }

        var tasks = await q.ToListAsync(ct);
        var users = await db.Users.AsNoTracking().Where(u => u.IsActive).ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var groups = tasks.GroupBy(t => t.OwnerId).Select(g =>
        {
            var oldest = g.OrderBy(t => t.DueAt).First();
            var days = (int)(now - oldest.DueAt!.Value).TotalDays;
            return new UserOverdueItem(g.Key, users.GetValueOrDefault(g.Key, "Unknown"), g.Count(), $"{days}d");
        }).OrderByDescending(x => x.OverdueCount).ToList();

        return new WidgetResult("team_overdue", "Team Overdue Tasks", WidgetSize.Sm, groups, "/activities?view=overdue");
    }
}

public sealed class RecentWinsWidget(AppDbContext db) : IDashboardWidget
{
    public string Key => "recent_wins";
    public AppModule Module => AppModule.Deals;
    public DashboardView[] Views => [DashboardView.Manager, DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Md;
    public int DefaultOrder => 130;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var q = db.Deals.AsNoTracking().Where(d => d.Status == "Won" && d.ClosedAt != null);
        if (ctx.ScopeUserIds is not null) q = q.Where(d => ctx.ScopeUserIds.Contains(d.OwnerId));

        var deals = await q.OrderByDescending(d => d.ClosedAt).Take(5).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var items = deals.Select(d => new RecentWinItem(
            d.Id,
            d.Name,
            d.Amount,
            d.Currency,
            d.Amount,
            users.GetValueOrDefault(d.OwnerId, "Owner"),
            d.ClosedAt!.Value
        )).ToList();

        return new WidgetResult("recent_wins", "Recent Closed Wins", WidgetSize.Md, items, "/deals");
    }
}

public sealed class ActivitySummaryWidget(AppDbContext db) : IDashboardWidget
{
    public string Key => "activity_summary";
    public AppModule Module => AppModule.Activities;
    public DashboardView[] Views => [DashboardView.Staff, DashboardView.Manager];
    public WidgetSize DefaultSize => WidgetSize.Md;
    public int DefaultOrder => 140;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var actsNow = await db.Activities.AsNoTracking()
            .Where(a => a.Status == "Completed" && a.CompletedAt != null && a.CompletedAt >= ctx.Period.StartUtc && a.CompletedAt < ctx.Period.EndUtc)
            .ToListAsync(ct);

        var actsPrev = await db.Activities.AsNoTracking()
            .Where(a => a.Status == "Completed" && a.CompletedAt != null && a.CompletedAt >= ctx.PreviousPeriod.StartUtc && a.CompletedAt < ctx.PreviousPeriod.EndUtc)
            .ToListAsync(ct);

        var types = new[] { "Task", "Call", "Meeting", "Note" };
        var list = types.Select(t => new ActivityTypeSummaryItem(
            t,
            actsNow.Count(a => a.Type == t),
            actsPrev.Count(a => a.Type == t)
        )).ToList();

        return new WidgetResult("activity_summary", "Completed Activities Summary", WidgetSize.Md, list, "/activities");
    }
}

public sealed class SystemOverviewWidget(AppDbContext db) : IDashboardWidget
{
    public string Key => "system_overview";
    public AppModule Module => AppModule.Users;
    public DashboardView[] Views => [DashboardView.Admin];
    public WidgetSize DefaultSize => WidgetSize.Sm;
    public int DefaultOrder => 150;

    public async Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct)
    {
        var activeUsers = await db.Users.CountAsync(u => u.IsActive, ct);
        var pendingInvites = await db.Invitations.CountAsync(i => i.AcceptedAt == null, ct);
        var storage = await db.Files.SumAsync(f => (long?)f.SizeBytes, ct) ?? 0L;

        var data = new SystemOverviewData(activeUsers, 10, pendingInvites, storage, 14, false);
        return new WidgetResult("system_overview", "System Health & Capacity", WidgetSize.Sm, data, "/settings/users");
    }
}
