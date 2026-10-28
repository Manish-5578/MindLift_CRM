namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 6 / #40: Custom dashboard widgets (dashboard-widgets).
/// Request to create or update a custom dashboard widget.
/// </summary>
public sealed record SaveCustomDashboardWidgetRequest(
    string Title,
    string WidgetType,
    string Source,
    string MetricAggregation = "count",
    string? TargetField = null,
    string? GroupByField = null,
    string? FilterJson = null,
    string Size = "md",
    bool IsShared = true);

/// <summary>
/// Configured custom dashboard widget definition.
/// </summary>
public sealed record CustomDashboardWidgetDto(
    Guid Id,
    string Title,
    string WidgetType,
    string Source,
    string MetricAggregation,
    string? TargetField,
    string? GroupByField,
    string? FilterJson,
    string Size,
    bool IsShared,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc);

/// <summary>
/// Evaluated runtime data for a custom widget.
/// </summary>
public sealed record CustomWidgetDataDto(
    Guid? WidgetId,
    string Title,
    string WidgetType,
    string Source,
    decimal? MetricValue,
    string? MetricFormatted,
    IReadOnlyList<CustomWidgetChartBucketDto>? ChartBuckets,
    IReadOnlyList<CustomWidgetRecordRowDto>? RecordRows);

public sealed record CustomWidgetChartBucketDto(
    string Label,
    decimal Value,
    int Count);

public sealed record CustomWidgetRecordRowDto(
    Guid Id,
    string Title,
    string? Subtitle,
    decimal? Value,
    string? Status,
    DateTime CreatedAtUtc);
