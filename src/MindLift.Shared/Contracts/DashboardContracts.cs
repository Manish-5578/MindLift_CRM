using System.Text.Json;
using MindLift.Shared.Enums;

namespace MindLift.Shared.Contracts;

public enum WidgetSize
{
    Sm = 3,
    Md = 6,
    Lg = 9,
    Xl = 12
}

public enum SalesTargetMetric
{
    WonAmount,
    WonDeals,
    NewLeads,
    CompletedActivities
}

public enum SalesTargetPeriod
{
    Month,
    Quarter,
    Year
}

public sealed record KpiTileDto(
    string Key,
    string Label,
    decimal Value,
    decimal? PreviousValue,
    decimal? ChangePct,
    string Format,
    string Trend,
    string GoodWhen,
    string? DrillDownUrl
);

public sealed record DashboardTaskItem(
    Guid Id,
    string Subject,
    DateTime? DueAt,
    string Priority,
    string? RelatedName,
    string? RelatedUrl,
    bool IsCompleted
);

public sealed record DashboardMeetingItem(
    Guid Id,
    string Subject,
    DateTime StartAt,
    DateTime EndAt,
    string? Location,
    string? RelatedName,
    string? RelatedUrl
);

public sealed record DashboardLeadItem(
    Guid Id,
    string Name,
    string CompanyName,
    string Status,
    string Rating,
    string Age,
    bool IsSlaBreached
);

public sealed record StageBreakdownItem(
    Guid StageId,
    string Name,
    string Color,
    int Count,
    decimal TotalInBase,
    decimal WeightedInBase
);

public sealed record DealsInProgressData(
    int Count,
    decimal TotalInBase,
    decimal WeightedInBase,
    int RottingCount,
    IReadOnlyList<StageBreakdownItem> ByStage
);

public sealed record ClosingDealItem(
    Guid Id,
    string Name,
    decimal Amount,
    string Currency,
    decimal AmountInBase,
    DateTime ExpectedCloseDate,
    string StageName,
    string? AccountName,
    bool IsOverdue
);

public sealed record TargetProgressItem(
    string Metric,
    decimal Target,
    decimal Actual,
    decimal Pct,
    decimal ExpectedPct,
    string Status
);

public sealed record MonthlySalesItem(
    string MonthLabel,
    decimal WonAmount,
    decimal? TargetAmount
);

public sealed record LeadSourceItem(
    string Source,
    int Count,
    int ConvertedCount
);

public sealed record FunnelStageItem(
    Guid StageId,
    string StageName,
    int Count,
    decimal TotalInBase,
    decimal ConversionRate
);

public sealed record LeaderboardUserItem(
    Guid UserId,
    string FullName,
    decimal WonAmount,
    int WonCount,
    int NewLeadsCount,
    int CompletedActivitiesCount,
    decimal WeightedPipeline,
    decimal TargetPct
);

public sealed record UserOverdueItem(
    Guid UserId,
    string FullName,
    int OverdueCount,
    string? OldestOverdueAge
);

public sealed record RecentWinItem(
    Guid DealId,
    string DealName,
    decimal Amount,
    string Currency,
    decimal AmountInBase,
    string OwnerName,
    DateTime ClosedAt
);

public sealed record ActivityTypeSummaryItem(
    string ActivityType,
    int Count,
    int PreviousCount
);

public sealed record SystemOverviewData(
    int ActiveUsers,
    int PlanSeats,
    int PendingInvites,
    long StorageBytes,
    int TrialDaysLeft,
    bool IsSampleData
);

public sealed record DashboardWidgetLayoutItem(
    string Key,
    int Order,
    WidgetSize Size,
    bool IsHidden
);

public sealed record DashboardWidgetDto(
    string Key,
    string Title,
    WidgetSize Size,
    int Order,
    bool IsHidden,
    JsonElement? Data,
    string? DrillDownUrl,
    string? Error
);

public sealed record DashboardResponse(
    DashboardView View,
    IReadOnlyList<DashboardView> AvailableViews,
    DateTime GeneratedAt,
    IReadOnlyList<DashboardWidgetDto> Widgets
);

public sealed record SaveDashboardLayoutRequest(
    DashboardView View,
    IReadOnlyList<DashboardWidgetLayoutItem> Widgets
);

public sealed record DashboardLayoutDto(
    DashboardView View,
    IReadOnlyList<DashboardWidgetLayoutItem> Widgets
);

public sealed record SalesTargetDto(
    Guid Id,
    Guid? UserId,
    string? UserName,
    Guid? TeamId,
    string? TeamName,
    SalesTargetMetric Metric,
    SalesTargetPeriod PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TargetValue
);

public sealed record SaveSalesTargetRequest(
    Guid? UserId,
    Guid? TeamId,
    SalesTargetMetric Metric,
    SalesTargetPeriod PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TargetValue
);
