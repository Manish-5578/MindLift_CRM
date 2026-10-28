using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 6 / #40: Custom dashboard widgets (dashboard-widgets).
/// User-configurable KPI metric cards, breakdown charts, and record list widgets.
/// </summary>
[Audited]
public sealed class CustomDashboardWidget : TenantEntity
{
    public string Title { get; set; } = string.Empty;
    public string WidgetType { get; set; } = "kpi_metric"; // kpi_metric, chart_breakdown, record_list
    public string Source { get; set; } = "deals"; // leads, deals, contacts, accounts, tickets, activities
    public string MetricAggregation { get; set; } = "count"; // count, sum, average
    public string? TargetField { get; set; }
    public string? GroupByField { get; set; }
    public string? FilterJson { get; set; }
    public string Size { get; set; } = "md"; // sm, md, lg, xl
    public bool IsShared { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
}
