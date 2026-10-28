using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Dashboard;

public sealed class CustomDashboardWidgetService(
    AppDbContext db,
    ITenantContext tenantContext) : ICustomDashboardWidgetService
{
    public async Task<IReadOnlyList<CustomDashboardWidgetDto>> ListWidgetsAsync(CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var userId = tenantContext.UserId ?? Guid.Empty;

        var widgets = await db.CustomDashboardWidgets.AsNoTracking()
            .Where(w => w.IsShared || w.CreatedByUserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);

        return widgets.Select(MapDto).ToList();
    }

    public async Task<CustomDashboardWidgetDto> GetWidgetAsync(Guid id, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var widget = await db.CustomDashboardWidgets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (widget == null)
            throw new NotFoundException("custom_widget_not_found", "Custom dashboard widget was not found.");

        return MapDto(widget);
    }

    public async Task<CustomDashboardWidgetDto> CreateWidgetAsync(SaveCustomDashboardWidgetRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        ValidateRequest(request);

        var widget = new CustomDashboardWidget
        {
            TenantId = tenantId,
            Title = request.Title.Trim(),
            WidgetType = request.WidgetType.Trim().ToLowerInvariant(),
            Source = request.Source.Trim().ToLowerInvariant(),
            MetricAggregation = request.MetricAggregation.Trim().ToLowerInvariant(),
            TargetField = request.TargetField?.Trim().ToLowerInvariant(),
            GroupByField = request.GroupByField?.Trim().ToLowerInvariant(),
            FilterJson = request.FilterJson,
            Size = string.IsNullOrWhiteSpace(request.Size) ? "md" : request.Size.Trim().ToLowerInvariant(),
            IsShared = request.IsShared,
            CreatedByUserId = tenantContext.UserId ?? Guid.Empty
        };

        db.CustomDashboardWidgets.Add(widget);
        await db.SaveChangesAsync(ct);

        return MapDto(widget);
    }

    public async Task<CustomDashboardWidgetDto> UpdateWidgetAsync(Guid id, SaveCustomDashboardWidgetRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        ValidateRequest(request);

        var widget = await db.CustomDashboardWidgets.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (widget == null)
            throw new NotFoundException("custom_widget_not_found", "Custom dashboard widget was not found.");

        widget.Title = request.Title.Trim();
        widget.WidgetType = request.WidgetType.Trim().ToLowerInvariant();
        widget.Source = request.Source.Trim().ToLowerInvariant();
        widget.MetricAggregation = request.MetricAggregation.Trim().ToLowerInvariant();
        widget.TargetField = request.TargetField?.Trim().ToLowerInvariant();
        widget.GroupByField = request.GroupByField?.Trim().ToLowerInvariant();
        widget.FilterJson = request.FilterJson;
        widget.Size = string.IsNullOrWhiteSpace(request.Size) ? "md" : request.Size.Trim().ToLowerInvariant();
        widget.IsShared = request.IsShared;

        await db.SaveChangesAsync(ct);

        return MapDto(widget);
    }

    public async Task<bool> DeleteWidgetAsync(Guid id, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var widget = await db.CustomDashboardWidgets.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (widget == null)
            throw new NotFoundException("custom_widget_not_found", "Custom dashboard widget was not found.");

        db.CustomDashboardWidgets.Remove(widget);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<CustomWidgetDataDto> EvaluateDataAsync(Guid id, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var widget = await db.CustomDashboardWidgets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (widget == null)
            throw new NotFoundException("custom_widget_not_found", "Custom dashboard widget was not found.");

        return await EvaluateAsync(
            widget.Id,
            widget.Title,
            widget.WidgetType,
            widget.Source,
            widget.MetricAggregation,
            widget.TargetField,
            widget.GroupByField,
            ct);
    }

    public async Task<CustomWidgetDataDto> PreviewDataAsync(SaveCustomDashboardWidgetRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        ValidateRequest(request);

        return await EvaluateAsync(
            null,
            request.Title,
            request.WidgetType.Trim().ToLowerInvariant(),
            request.Source.Trim().ToLowerInvariant(),
            request.MetricAggregation.Trim().ToLowerInvariant(),
            request.TargetField?.Trim().ToLowerInvariant(),
            request.GroupByField?.Trim().ToLowerInvariant(),
            ct);
    }

    private async Task<CustomWidgetDataDto> EvaluateAsync(
        Guid? widgetId,
        string title,
        string widgetType,
        string source,
        string aggregation,
        string? targetField,
        string? groupByField,
        CancellationToken ct)
    {
        decimal? metricVal = null;
        string? formatted = null;
        List<CustomWidgetChartBucketDto>? buckets = null;
        List<CustomWidgetRecordRowDto>? rows = null;

        switch (source)
        {
            case "deals":
                var deals = await db.Deals.AsNoTracking().ToListAsync(ct);
                if (widgetType == "kpi_metric")
                {
                    if (aggregation == "sum")
                    {
                        metricVal = deals.Sum(d => d.Amount);
                        formatted = FormattableString.Invariant($"${metricVal:N0}");
                    }
                    else if (aggregation == "average")
                    {
                        metricVal = deals.Count > 0 ? Math.Round((decimal)deals.Average(d => d.Amount), 2) : 0m;
                        formatted = FormattableString.Invariant($"${metricVal:N0}");
                    }
                    else
                    {
                        metricVal = deals.Count;
                        formatted = FormattableString.Invariant($"{metricVal:N0}");
                    }
                }
                else if (widgetType == "chart_breakdown")
                {
                    buckets = deals
                        .GroupBy(d => string.IsNullOrWhiteSpace(d.Status) ? "Unassigned" : d.Status)
                        .Select(g => new CustomWidgetChartBucketDto(g.Key, g.Sum(x => x.Amount), g.Count()))
                        .ToList();
                }
                else
                {
                    rows = deals
                        .OrderByDescending(d => d.CreatedAt)
                        .Take(5)
                        .Select(d => new CustomWidgetRecordRowDto(d.Id, d.Name, $"Amount: ${d.Amount:N0}", d.Amount, d.Status, d.CreatedAt))
                        .ToList();
                }
                break;

            case "leads":
                var leads = await db.Leads.AsNoTracking().ToListAsync(ct);
                if (widgetType == "kpi_metric")
                {
                    metricVal = leads.Count;
                    formatted = $"{metricVal:N0}";
                }
                else if (widgetType == "chart_breakdown")
                {
                    buckets = leads
                        .GroupBy(l => string.IsNullOrWhiteSpace(l.Status) ? "New" : l.Status)
                        .Select(g => new CustomWidgetChartBucketDto(g.Key, g.Count(), g.Count()))
                        .ToList();
                }
                else
                {
                    rows = leads
                        .OrderByDescending(l => l.CreatedAt)
                        .Take(5)
                        .Select(l => new CustomWidgetRecordRowDto(l.Id, $"{l.FirstName} {l.LastName}".Trim(), l.CompanyName, null, l.Status, l.CreatedAt))
                        .ToList();
                }
                break;

            case "tickets":
                var tickets = await db.Tickets.AsNoTracking().ToListAsync(ct);
                if (widgetType == "kpi_metric")
                {
                    metricVal = tickets.Count(t => t.Status != "closed");
                    formatted = $"{metricVal:N0}";
                }
                else if (widgetType == "chart_breakdown")
                {
                    buckets = tickets
                        .GroupBy(t => string.IsNullOrWhiteSpace(t.Priority) ? "normal" : t.Priority)
                        .Select(g => new CustomWidgetChartBucketDto(g.Key, g.Count(), g.Count()))
                        .ToList();
                }
                else
                {
                    rows = tickets
                        .Where(t => t.Status != "closed")
                        .OrderByDescending(t => t.CreatedAt)
                        .Take(5)
                        .Select(t => new CustomWidgetRecordRowDto(t.Id, t.Subject, $"Priority: {t.Priority}", null, t.Status, t.CreatedAt))
                        .ToList();
                }
                break;

            case "accounts":
                var accounts = await db.Accounts.AsNoTracking().ToListAsync(ct);
                if (widgetType == "kpi_metric")
                {
                    metricVal = accounts.Count;
                    formatted = $"{metricVal:N0}";
                }
                else if (widgetType == "chart_breakdown")
                {
                    buckets = accounts
                        .GroupBy(a => string.IsNullOrWhiteSpace(a.Industry) ? "Other" : a.Industry)
                        .Select(g => new CustomWidgetChartBucketDto(g.Key, g.Count(), g.Count()))
                        .ToList();
                }
                else
                {
                    rows = accounts
                        .OrderByDescending(a => a.CreatedAt)
                        .Take(5)
                        .Select(a => new CustomWidgetRecordRowDto(a.Id, a.Name, a.Industry, null, "Active", a.CreatedAt))
                        .ToList();
                }
                break;

            default:
                metricVal = 0m;
                formatted = "0";
                break;
        }

        return new CustomWidgetDataDto(
            WidgetId: widgetId,
            Title: title,
            WidgetType: widgetType,
            Source: source,
            MetricValue: metricVal,
            MetricFormatted: formatted,
            ChartBuckets: buckets,
            RecordRows: rows);
    }

    private static void ValidateRequest(SaveCustomDashboardWidgetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ValidationException("title", "Widget title is required.");

        var validSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "deals", "leads", "contacts", "accounts", "tickets", "activities" };
        if (!validSources.Contains(request.Source.Trim()))
            throw new ValidationException("source", "Invalid data source. Choose from deals, leads, contacts, accounts, tickets, or activities.");

        var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kpi_metric", "chart_breakdown", "record_list" };
        if (!validTypes.Contains(request.WidgetType.Trim()))
            throw new ValidationException("widgetType", "Invalid widget type. Choose from kpi_metric, chart_breakdown, or record_list.");
    }

    private static CustomDashboardWidgetDto MapDto(CustomDashboardWidget w) => new(
        w.Id,
        w.Title,
        w.WidgetType,
        w.Source,
        w.MetricAggregation,
        w.TargetField,
        w.GroupByField,
        w.FilterJson,
        w.Size,
        w.IsShared,
        w.CreatedByUserId,
        w.CreatedAt);
}
