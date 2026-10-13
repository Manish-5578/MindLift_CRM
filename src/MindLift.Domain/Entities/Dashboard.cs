using System.Text.Json;
using MindLift.Domain.Common;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Domain.Entities;

/// <summary>
/// Custom user dashboard layout and widget ordering per view (Staff, Manager, Admin).
/// </summary>
[Audited]
public sealed class DashboardLayout : TenantEntity
{
    public Guid UserId { get; set; }
    public DashboardView View { get; set; }

    /// <summary>JSON array of DashboardWidgetLayoutItem.</summary>
    public JsonDocument Layout { get; set; } = JsonDocument.Parse("[]");
}

/// <summary>
/// Sales and activity target for a user, team, or company across a period.
/// </summary>
[Audited]
public sealed class SalesTarget : TenantEntity
{
    public Guid? UserId { get; set; }
    public Guid? TeamId { get; set; }
    public SalesTargetMetric Metric { get; set; }
    public SalesTargetPeriod PeriodType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal TargetValue { get; set; }
}
