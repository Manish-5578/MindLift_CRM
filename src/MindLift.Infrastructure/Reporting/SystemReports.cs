using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Reporting;

public sealed record SystemReportContext(
    Guid TenantId,
    IReadOnlyList<Guid>? ScopeUserIds,
    TimeZoneInfo TimeZone,
    DateRange DateRange,
    Dictionary<string, string> Parameters);

public interface ISystemReport
{
    string Key { get; }
    string Name { get; }
    string Description { get; }
    AppModule Module { get; }
    ReportChartType DefaultChartType { get; }
    Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default);
}

public interface ISystemReportRegistry
{
    ISystemReport? Get(string key);
    IReadOnlyList<ISystemReport> GetAll();
}

public sealed class SystemReportRegistry : ISystemReportRegistry
{
    private readonly Dictionary<string, ISystemReport> _reports;

    public SystemReportRegistry(AppDbContext db, IDataScopeService scope)
    {
        var reports = new ISystemReport[]
        {
            new SalesByUserReport(db),
            new LeadSourcesReport(db),
            new LeadConversionReport(db),
            new RevenueByMonthReport(db),
            new PipelineValueReport(db),
            new StageFunnelReport(db),
            new DealVelocityReport(db),
            new SalesForecastReport(db),
            new LostReasonsReport(db),
            new OverdueTasksReport(db),
            new ActivitiesByUserReport(db),
            new TargetVsActualReport(db),
            new NewCustomersReport(db, scope)
        };

        _reports = reports.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
    }

    public ISystemReport? Get(string key) => _reports.GetValueOrDefault(key);
    public IReadOnlyList<ISystemReport> GetAll() => _reports.Values.ToList();
}

// 1. Sales by user
public sealed class SalesByUserReport(AppDbContext db) : ISystemReport
{
    public string Key => "sales_by_user";
    public string Name => "Sales by User";
    public string Description => "Won deals, lost deals, win rate, and open pipeline per sales rep.";
    public AppModule Module => AppModule.Deals;
    public ReportChartType DefaultChartType => ReportChartType.Bar;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    COALESCE(concat_ws(' ', u.first_name, u.last_name), 'Unassigned') AS owner_name,
    COUNT(CASE WHEN d.status = 'Won' AND d.closed_at >= @from AND d.closed_at < @to THEN 1 END) AS won_count,
    COALESCE(SUM(CASE WHEN d.status = 'Won' AND d.closed_at >= @from AND d.closed_at < @to THEN d.amount ELSE 0 END), 0) AS won_amount,
    COUNT(CASE WHEN d.status = 'Lost' AND d.closed_at >= @from AND d.closed_at < @to THEN 1 END) AS lost_count,
    ROUND(
        CASE WHEN (COUNT(CASE WHEN d.status = 'Won' AND d.closed_at >= @from AND d.closed_at < @to THEN 1 END) + COUNT(CASE WHEN d.status = 'Lost' AND d.closed_at >= @from AND d.closed_at < @to THEN 1 END)) > 0
        THEN (COUNT(CASE WHEN d.status = 'Won' AND d.closed_at >= @from AND d.closed_at < @to THEN 1 END)::numeric / 
              (COUNT(CASE WHEN d.status = 'Won' AND d.closed_at >= @from AND d.closed_at < @to THEN 1 END) + COUNT(CASE WHEN d.status = 'Lost' AND d.closed_at >= @from AND d.closed_at < @to THEN 1 END)) * 100)
        ELSE 0 END, 1
    ) AS win_rate_pct,
    COALESCE(SUM(CASE WHEN d.status = 'Open' THEN d.amount ELSE 0 END), 0) AS open_pipeline,
    COALESCE(SUM(CASE WHEN d.status = 'Open' THEN (d.amount * d.probability / 100.0) ELSE 0 END), 0) AS weighted_pipeline
FROM users u
LEFT JOIN deals d ON d.owner_id = u.id AND d.tenant_id = @tenantId AND d.is_deleted = false
WHERE u.tenant_id = @tenantId AND u.is_active = true
  AND (@allScope OR u.id = ANY(@scopeUserIds))
GROUP BY u.id, u.first_name, u.last_name
ORDER BY won_amount DESC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("from", ctx.DateRange.StartUtc);
        p.Add("to", ctx.DateRange.EndUtc);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "owner_name", Label = "Owner", Type = "Text", IsGroup = true },
            new() { Key = "won_count", Label = "Won Deals", Type = "Number", IsAggregate = true },
            new() { Key = "won_amount", Label = "Won Revenue", Type = "Currency", Format = "currency", IsAggregate = true },
            new() { Key = "lost_count", Label = "Lost Deals", Type = "Number", IsAggregate = true },
            new() { Key = "win_rate_pct", Label = "Win Rate %", Type = "Percent", Format = "percent", IsAggregate = true },
            new() { Key = "open_pipeline", Label = "Open Pipeline", Type = "Currency", Format = "currency", IsAggregate = true },
            new() { Key = "weighted_pipeline", Label = "Weighted Pipeline", Type = "Currency", Format = "currency", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "owner_name", SeriesFields = ["won_amount"] }
        };
    }
}

// 2. Lead sources
public sealed class LeadSourcesReport(AppDbContext db) : ISystemReport
{
    public string Key => "lead_sources";
    public string Name => "Lead Sources";
    public string Description => "Volume, conversion count, and conversion rate by acquisition source.";
    public AppModule Module => AppModule.Leads;
    public ReportChartType DefaultChartType => ReportChartType.Donut;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    COALESCE(l.source, 'Unknown') AS source_name,
    COUNT(l.id) AS total_leads,
    COUNT(CASE WHEN l.converted_at IS NOT NULL THEN 1 END) AS converted_leads,
    ROUND(
        CASE WHEN COUNT(l.id) > 0 
        THEN (COUNT(CASE WHEN l.converted_at IS NOT NULL THEN 1 END)::numeric / COUNT(l.id) * 100) 
        ELSE 0 END, 1
    ) AS conversion_rate_pct
FROM leads l
WHERE l.tenant_id = @tenantId AND l.is_deleted = false
  AND l.created_at >= @from AND l.created_at < @to
  AND (@allScope OR l.owner_id = ANY(@scopeUserIds))
GROUP BY l.source
ORDER BY total_leads DESC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("from", ctx.DateRange.StartUtc);
        p.Add("to", ctx.DateRange.EndUtc);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "source_name", Label = "Source", Type = "Picklist", IsGroup = true },
            new() { Key = "total_leads", Label = "Total Leads", Type = "Number", IsAggregate = true },
            new() { Key = "converted_leads", Label = "Converted", Type = "Number", IsAggregate = true },
            new() { Key = "conversion_rate_pct", Label = "Conversion %", Type = "Percent", Format = "percent", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "source_name", SeriesFields = ["total_leads"] }
        };
    }
}

// 3. Lead conversion rate (monthly cohort)
public sealed class LeadConversionReport(AppDbContext db) : ISystemReport
{
    public string Key => "lead_conversion";
    public string Name => "Lead Conversion Rate";
    public string Description => "Monthly cohort lead conversion rate and average days to convert.";
    public AppModule Module => AppModule.Leads;
    public ReportChartType DefaultChartType => ReportChartType.Line;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    to_char(date_trunc('month', (l.created_at AT TIME ZONE @tz)), 'YYYY-MM') AS cohort_month,
    COUNT(l.id) AS leads_created,
    COUNT(CASE WHEN l.converted_at IS NOT NULL THEN 1 END) AS leads_converted,
    ROUND(
        CASE WHEN COUNT(l.id) > 0 
        THEN (COUNT(CASE WHEN l.converted_at IS NOT NULL THEN 1 END)::numeric / COUNT(l.id) * 100) 
        ELSE 0 END, 1
    ) AS conversion_rate_pct,
    ROUND(COALESCE(AVG(CASE WHEN l.converted_at IS NOT NULL THEN EXTRACT(EPOCH FROM (l.converted_at - l.created_at)) / 86400.0 END), 0), 1) AS avg_days_to_convert
FROM leads l
WHERE l.tenant_id = @tenantId AND l.is_deleted = false
  AND l.created_at >= @from AND l.created_at < @to
  AND (@allScope OR l.owner_id = ANY(@scopeUserIds))
GROUP BY 1
ORDER BY 1 ASC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("tz", ctx.TimeZone.Id);
        p.Add("from", ctx.DateRange.StartUtc);
        p.Add("to", ctx.DateRange.EndUtc);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "cohort_month", Label = "Month", Type = "Date", IsGroup = true },
            new() { Key = "leads_created", Label = "Created", Type = "Number", IsAggregate = true },
            new() { Key = "leads_converted", Label = "Converted", Type = "Number", IsAggregate = true },
            new() { Key = "conversion_rate_pct", Label = "Conversion %", Type = "Percent", Format = "percent", IsAggregate = true },
            new() { Key = "avg_days_to_convert", Label = "Avg Days to Convert", Type = "Number", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "cohort_month", SeriesFields = ["conversion_rate_pct"] }
        };
    }
}

// 4. Revenue by month
public sealed class RevenueByMonthReport(AppDbContext db) : ISystemReport
{
    public string Key => "revenue_by_month";
    public string Name => "Revenue by Month";
    public string Description => "Monthly closed won revenue and deal count.";
    public AppModule Module => AppModule.Deals;
    public ReportChartType DefaultChartType => ReportChartType.Column;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    to_char(date_trunc('month', (d.closed_at AT TIME ZONE @tz)), 'YYYY-MM') AS closed_month,
    COALESCE(SUM(d.amount), 0) AS revenue,
    COUNT(d.id) AS deals_won,
    ROUND(COALESCE(AVG(d.amount), 0), 2) AS avg_deal_size
FROM deals d
WHERE d.tenant_id = @tenantId AND d.is_deleted = false AND d.status = 'Won'
  AND d.closed_at >= @from AND d.closed_at < @to
  AND (@allScope OR d.owner_id = ANY(@scopeUserIds))
GROUP BY 1
ORDER BY 1 ASC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("tz", ctx.TimeZone.Id);
        p.Add("from", ctx.DateRange.StartUtc);
        p.Add("to", ctx.DateRange.EndUtc);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "closed_month", Label = "Month", Type = "Date", IsGroup = true },
            new() { Key = "revenue", Label = "Revenue", Type = "Currency", Format = "currency", IsAggregate = true },
            new() { Key = "deals_won", Label = "Deals Won", Type = "Number", IsAggregate = true },
            new() { Key = "avg_deal_size", Label = "Avg Deal Size", Type = "Currency", Format = "currency", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "closed_month", SeriesFields = ["revenue"] }
        };
    }
}

// 5. Pipeline value
public sealed class PipelineValueReport(AppDbContext db) : ISystemReport
{
    public string Key => "pipeline_value";
    public string Name => "Pipeline Value";
    public string Description => "Open deal count, total amount, and weighted value by pipeline stage.";
    public AppModule Module => AppModule.Deals;
    public ReportChartType DefaultChartType => ReportChartType.StackedBar;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    s.name AS stage_name,
    COUNT(d.id) AS deal_count,
    COALESCE(SUM(d.amount), 0) AS total_amount,
    COALESCE(SUM(d.amount * d.probability / 100.0), 0) AS weighted_amount
FROM pipeline_stages s
LEFT JOIN deals d ON d.stage_id = s.id AND d.tenant_id = @tenantId AND d.is_deleted = false AND d.status = 'Open'
  AND (@allScope OR d.owner_id = ANY(@scopeUserIds))
WHERE s.tenant_id = @tenantId AND s.is_deleted = false AND s.stage_type = 'Open'
GROUP BY s.id, s.name, s.sort_order
ORDER BY s.sort_order ASC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "stage_name", Label = "Stage", Type = "Text", IsGroup = true },
            new() { Key = "deal_count", Label = "Open Deals", Type = "Number", IsAggregate = true },
            new() { Key = "total_amount", Label = "Total Value", Type = "Currency", Format = "currency", IsAggregate = true },
            new() { Key = "weighted_amount", Label = "Weighted Value", Type = "Currency", Format = "currency", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "stage_name", SeriesFields = ["total_amount", "weighted_amount"] }
        };
    }
}

// 6. Stage funnel
public sealed class StageFunnelReport(AppDbContext db) : ISystemReport
{
    public string Key => "stage_funnel";
    public string Name => "Sales Funnel (Stage Conversion)";
    public string Description => "Deals reached each stage in sequence and drop-off rate.";
    public AppModule Module => AppModule.Deals;
    public ReportChartType DefaultChartType => ReportChartType.Funnel;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
WITH cohort AS (
  SELECT d.id FROM deals d
  WHERE d.tenant_id = @tenantId AND d.is_deleted = false
    AND d.created_at >= @from AND d.created_at < @to
    AND (@allScope OR d.owner_id = ANY(@scopeUserIds))
),
reached AS (
  SELECT DISTINCT h.deal_id, s.sort_order
  FROM deal_stage_history h
  JOIN pipeline_stages s ON s.id = h.to_stage_id
  WHERE h.tenant_id = @tenantId AND h.deal_id IN (SELECT id FROM cohort)
    AND s.stage_type <> 'Lost'
),
max_reached AS (
  SELECT deal_id, MAX(sort_order) AS max_order FROM reached GROUP BY deal_id
)
SELECT 
    s.name AS stage_name,
    s.sort_order,
    (SELECT COUNT(*) FROM max_reached m WHERE m.max_order >= s.sort_order) AS deals_reached
FROM pipeline_stages s
WHERE s.tenant_id = @tenantId AND s.is_deleted = false AND s.stage_type <> 'Lost'
ORDER BY s.sort_order ASC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("from", ctx.DateRange.StartUtc);
        p.Add("to", ctx.DateRange.EndUtc);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "stage_name", Label = "Stage", Type = "Text", IsGroup = true },
            new() { Key = "deals_reached", Label = "Deals Reached", Type = "Number", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "stage_name", SeriesFields = ["deals_reached"] }
        };
    }
}

// 7. Deal velocity
public sealed class DealVelocityReport(AppDbContext db) : ISystemReport
{
    public string Key => "deal_velocity";
    public string Name => "Deal Velocity";
    public string Description => "Average days spent in each stage and total sales cycle duration.";
    public AppModule Module => AppModule.Deals;
    public ReportChartType DefaultChartType => ReportChartType.Bar;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    s.name AS stage_name,
    COUNT(h.id) AS transitions,
    ROUND(COALESCE(AVG(h.seconds_in_from_stage) / 86400.0, 0), 1) AS avg_days_in_stage
FROM deal_stage_history h
JOIN pipeline_stages s ON s.id = h.from_stage_id
JOIN deals d ON d.id = h.deal_id
WHERE h.tenant_id = @tenantId
  AND h.moved_at >= @from AND h.moved_at < @to
  AND (@allScope OR d.owner_id = ANY(@scopeUserIds))
GROUP BY s.id, s.name, s.sort_order
ORDER BY s.sort_order ASC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("from", ctx.DateRange.StartUtc);
        p.Add("to", ctx.DateRange.EndUtc);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "stage_name", Label = "Stage", Type = "Text", IsGroup = true },
            new() { Key = "transitions", Label = "Deals Moved", Type = "Number", IsAggregate = true },
            new() { Key = "avg_days_in_stage", Label = "Avg Days in Stage", Type = "Number", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "stage_name", SeriesFields = ["avg_days_in_stage"] }
        };
    }
}

// 8. Sales forecast
public sealed class SalesForecastReport(AppDbContext db) : ISystemReport
{
    public string Key => "sales_forecast";
    public string Name => "Sales Forecast";
    public string Description => "Projected deal revenue by expected close month.";
    public AppModule Module => AppModule.Deals;
    public ReportChartType DefaultChartType => ReportChartType.StackedColumn;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    to_char(date_trunc('month', (d.expected_close_date::timestamp AT TIME ZONE @tz)), 'YYYY-MM') AS expected_month,
    COUNT(d.id) AS deal_count,
    COALESCE(SUM(d.amount), 0) AS total_amount,
    COALESCE(SUM(d.amount * d.probability / 100.0), 0) AS weighted_amount
FROM deals d
WHERE d.tenant_id = @tenantId AND d.is_deleted = false AND d.status = 'Open'
  AND d.expected_close_date IS NOT NULL
  AND (@allScope OR d.owner_id = ANY(@scopeUserIds))
GROUP BY 1
ORDER BY 1 ASC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("tz", ctx.TimeZone.Id);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "expected_month", Label = "Expected Month", Type = "Date", IsGroup = true },
            new() { Key = "deal_count", Label = "Deals", Type = "Number", IsAggregate = true },
            new() { Key = "total_amount", Label = "Forecast Amount", Type = "Currency", Format = "currency", IsAggregate = true },
            new() { Key = "weighted_amount", Label = "Weighted Amount", Type = "Currency", Format = "currency", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "expected_month", SeriesFields = ["total_amount", "weighted_amount"] }
        };
    }
}

// 9. Lost reasons
public sealed class LostReasonsReport(AppDbContext db) : ISystemReport
{
    public string Key => "lost_reasons";
    public string Name => "Lost Deal Reasons";
    public string Description => "Deals lost breakdown by primary lost reason.";
    public AppModule Module => AppModule.Deals;
    public ReportChartType DefaultChartType => ReportChartType.Donut;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    COALESCE(d.lost_reason, 'Unspecified') AS reason_name,
    COUNT(d.id) AS lost_deals,
    COALESCE(SUM(d.amount), 0) AS lost_amount
FROM deals d
WHERE d.tenant_id = @tenantId AND d.is_deleted = false AND d.status = 'Lost'
  AND d.closed_at >= @from AND d.closed_at < @to
  AND (@allScope OR d.owner_id = ANY(@scopeUserIds))
GROUP BY d.lost_reason
ORDER BY lost_deals DESC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("from", ctx.DateRange.StartUtc);
        p.Add("to", ctx.DateRange.EndUtc);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "reason_name", Label = "Reason", Type = "Picklist", IsGroup = true },
            new() { Key = "lost_deals", Label = "Lost Deals", Type = "Number", IsAggregate = true },
            new() { Key = "lost_amount", Label = "Lost Value", Type = "Currency", Format = "currency", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "reason_name", SeriesFields = ["lost_deals"] }
        };
    }
}

// 10. Overdue tasks
public sealed class OverdueTasksReport(AppDbContext db) : ISystemReport
{
    public string Key => "overdue_tasks";
    public string Name => "Overdue Tasks";
    public string Description => "Open tasks past their due date grouped by assignee.";
    public AppModule Module => AppModule.Activities;
    public ReportChartType DefaultChartType => ReportChartType.Table;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    x.subject,
    COALESCE(concat_ws(' ', u.first_name, u.last_name), 'Unassigned') AS owner_name,
    x.due_at,
    ROUND(EXTRACT(EPOCH FROM (now() - x.due_at)) / 86400.0, 1) AS days_overdue,
    x.priority
FROM activities x
LEFT JOIN users u ON u.id = x.owner_id
WHERE x.tenant_id = @tenantId AND x.is_deleted = false
  AND x.type = 'Task' AND x.status = 'Open' AND x.due_at < now()
  AND (@allScope OR x.owner_id = ANY(@scopeUserIds))
ORDER BY x.due_at ASC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "subject", Label = "Task Subject", Type = "Text" },
            new() { Key = "owner_name", Label = "Assignee", Type = "Text", IsGroup = true },
            new() { Key = "due_at", Label = "Due Date", Type = "DateTime" },
            new() { Key = "days_overdue", Label = "Days Overdue", Type = "Number", IsAggregate = true },
            new() { Key = "priority", Label = "Priority", Type = "Picklist" }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType }
        };
    }
}

// 11. Activities by user
public sealed class ActivitiesByUserReport(AppDbContext db) : ISystemReport
{
    public string Key => "activities_by_user";
    public string Name => "Activities by User";
    public string Description => "Completed tasks, calls, and meetings per team member.";
    public AppModule Module => AppModule.Activities;
    public ReportChartType DefaultChartType => ReportChartType.StackedBar;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    COALESCE(concat_ws(' ', u.first_name, u.last_name), 'Unassigned') AS owner_name,
    COUNT(CASE WHEN x.type = 'Call' THEN 1 END) AS calls,
    COUNT(CASE WHEN x.type = 'Meeting' THEN 1 END) AS meetings,
    COUNT(CASE WHEN x.type = 'Task' THEN 1 END) AS tasks,
    COUNT(CASE WHEN x.type = 'Email' THEN 1 END) AS emails,
    COUNT(x.id) AS total_completed
FROM users u
LEFT JOIN activities x ON x.owner_id = u.id AND x.tenant_id = @tenantId AND x.is_deleted = false AND x.status = 'Completed'
  AND x.completed_at >= @from AND x.completed_at < @to
WHERE u.tenant_id = @tenantId AND u.is_active = true
  AND (@allScope OR u.id = ANY(@scopeUserIds))
GROUP BY u.id, u.first_name, u.last_name
ORDER BY total_completed DESC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("from", ctx.DateRange.StartUtc);
        p.Add("to", ctx.DateRange.EndUtc);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "owner_name", Label = "User", Type = "Text", IsGroup = true },
            new() { Key = "calls", Label = "Calls", Type = "Number", IsAggregate = true },
            new() { Key = "meetings", Label = "Meetings", Type = "Number", IsAggregate = true },
            new() { Key = "tasks", Label = "Tasks", Type = "Number", IsAggregate = true },
            new() { Key = "emails", Label = "Emails", Type = "Number", IsAggregate = true },
            new() { Key = "total_completed", Label = "Total Completed", Type = "Number", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "owner_name", SeriesFields = ["calls", "meetings", "tasks", "emails"] }
        };
    }
}

// 12. Target vs actual
public sealed class TargetVsActualReport(AppDbContext db) : ISystemReport
{
    public string Key => "target_vs_actual";
    public string Name => "Sales Target vs Actual";
    public string Description => "Pacing progress towards quota targets.";
    public AppModule Module => AppModule.Deals;
    public ReportChartType DefaultChartType => ReportChartType.Bar;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
SELECT 
    COALESCE(concat_ws(' ', u.first_name, u.last_name), 'Company') AS target_holder,
    t.metric::text AS metric_name,
    t.target_value,
    COALESCE(SUM(d.amount), 0) AS actual_value,
    ROUND(
        CASE WHEN t.target_value > 0 
        THEN (COALESCE(SUM(d.amount), 0) / t.target_value * 100) 
        ELSE 0 END, 1
    ) AS progress_pct
FROM sales_targets t
LEFT JOIN users u ON u.id = t.user_id
LEFT JOIN deals d ON d.owner_id = t.user_id AND d.tenant_id = @tenantId AND d.is_deleted = false AND d.status = 'Won'
  AND d.closed_at >= @from AND d.closed_at < @to
WHERE t.tenant_id = @tenantId AND t.is_deleted = false
  AND (@allScope OR t.user_id IS NULL OR t.user_id = ANY(@scopeUserIds))
GROUP BY t.id, u.first_name, u.last_name, t.metric, t.target_value
ORDER BY progress_pct DESC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("from", ctx.DateRange.StartUtc);
        p.Add("to", ctx.DateRange.EndUtc);
        p.Add("allScope", ctx.ScopeUserIds == null);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "target_holder", Label = "User / Scope", Type = "Text", IsGroup = true },
            new() { Key = "metric_name", Label = "Metric", Type = "Picklist" },
            new() { Key = "target_value", Label = "Target", Type = "Currency", Format = "currency", IsAggregate = true },
            new() { Key = "actual_value", Label = "Actual Won", Type = "Currency", Format = "currency", IsAggregate = true },
            new() { Key = "progress_pct", Label = "% Attained", Type = "Percent", Format = "percent", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "target_holder", SeriesFields = ["target_value", "actual_value"] }
        };
    }
}

// 13. New customers
public sealed class NewCustomersReport(AppDbContext db, IDataScopeService scope) : ISystemReport
{
    public string Key => "new_customers";
    public string Name => "New Customers (First Win)";
    public string Description => "Accounts or contacts whose first deal was won in the period.";
    public AppModule Module => AppModule.Contacts;
    public ReportChartType DefaultChartType => ReportChartType.Column;

    public async Task<ReportResult> RunAsync(SystemReportContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var sql = @"
WITH first_wins AS (
  SELECT 
    d.account_id,
    MIN(d.closed_at) AS first_won_at
  FROM deals d
  WHERE d.tenant_id = @tenantId AND d.is_deleted = false AND d.status = 'Won' AND d.account_id IS NOT NULL
  GROUP BY d.account_id
)
SELECT 
    to_char(date_trunc('month', (fw.first_won_at AT TIME ZONE @tz)), 'YYYY-MM') AS month_acquired,
    COUNT(a.id) AS new_customers,
    COALESCE(SUM(d.amount), 0) AS initial_revenue
FROM first_wins fw
JOIN accounts a ON a.id = fw.account_id AND a.tenant_id = @tenantId AND a.is_deleted = false
LEFT JOIN deals d ON d.account_id = a.id AND d.closed_at = fw.first_won_at AND d.status = 'Won'
  AND d.is_deleted = false AND (@allDealScope OR d.owner_id = ANY(@dealScopeUserIds))
WHERE fw.first_won_at >= @from AND fw.first_won_at < @to
  AND (@allScope OR a.owner_id = ANY(@scopeUserIds))
GROUP BY 1
ORDER BY 1 ASC;";

        var p = new DynamicParameters();
        p.Add("tenantId", ctx.TenantId);
        p.Add("tz", ctx.TimeZone.Id);
        p.Add("from", ctx.DateRange.StartUtc);
        p.Add("to", ctx.DateRange.EndUtc);
        p.Add("allScope", ctx.ScopeUserIds == null);
        // Revenue comes from deals, so it follows the Deals scope, not the Contacts scope.
        var dealScope = await scope.GetScopeUserIdsAsync(AppModule.Deals, ct);
        p.Add("allDealScope", dealScope == null);
        p.Add("dealScopeUserIds", dealScope?.ToArray() ?? []);
        p.Add("scopeUserIds", ctx.ScopeUserIds?.ToArray() ?? []);

        var rawRows = (await conn.QueryAsync(sql, p)).ToList();
        sw.Stop();

        var columns = new List<ReportColumn>
        {
            new() { Key = "month_acquired", Label = "Month", Type = "Date", IsGroup = true },
            new() { Key = "new_customers", Label = "New Customers", Type = "Number", IsAggregate = true },
            new() { Key = "initial_revenue", Label = "Initial Revenue", Type = "Currency", Format = "currency", IsAggregate = true }
        };

        var rows = rawRows.Select(r =>
        {
            var d = (IDictionary<string, object>)r;
            return columns.ToDictionary(c => c.Key, c => d.TryGetValue(c.Key, out var v) ? v : null);
        }).ToList();

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = new ReportVisualization { ChartType = DefaultChartType, XAxisField = "month_acquired", SeriesFields = ["new_customers"] }
        };
    }
}
