using System.Text.Json;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;

namespace MindLift.Shared.Contracts;

public enum ReportType
{
    Tabular = 0,
    Summary = 1,
    Matrix = 2
}

public enum AggregateFunc
{
    Count = 0,
    CountDistinct = 1,
    Sum = 2,
    Avg = 3,
    Min = 4,
    Max = 5
}

public enum DateGrouping
{
    Day = 0,
    Week = 1,
    Month = 2,
    Quarter = 3,
    Year = 4
}

public enum ReportVisibility
{
    Private = 0,
    Team = 1,
    Everyone = 2
}

public enum ReportChartType
{
    None = 0,
    Table = 1,
    Bar = 2,
    Column = 3,
    StackedBar = 4,
    StackedColumn = 5,
    Line = 6,
    Area = 7,
    Donut = 8,
    Pie = 9,
    Funnel = 10,
    Kpi = 11
}

public sealed record ReportGroup(string Field, DateGrouping? DateGrouping = null);

public sealed record ReportAggregate(string Field, AggregateFunc Func, string? Alias = null);

public sealed record ReportDateFilter(string Field, string? Preset = null, DateTime? From = null, DateTime? To = null);

public sealed record ReportFormula(string Expression, string Label, string? Format = null);

public sealed record ReportSort(string Field, bool Descending = false);

public sealed class ReportVisualization
{
    public ReportChartType ChartType { get; set; } = ReportChartType.Table;
    public string? XAxisField { get; set; }
    public List<string> SeriesFields { get; set; } = [];
}

public sealed class ReportColumn
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "Text";
    public string? Format { get; set; }
    public bool IsGroup { get; set; }
    public bool IsAggregate { get; set; }
}

public sealed class ReportQuery
{
    public string Source { get; set; } = string.Empty;
    public ReportType Type { get; set; } = ReportType.Tabular;
    public List<string> Columns { get; set; } = [];
    public List<ReportGroup> GroupBy { get; set; } = [];
    public List<ReportAggregate> Aggregates { get; set; } = [];
    public FilterGroup? Filter { get; set; }
    public ReportDateFilter? DateFilter { get; set; }
    public List<ReportSort> Sort { get; set; } = [];
    public int? Limit { get; set; }
    public List<ReportFormula> Formulas { get; set; } = [];
    public ReportVisualization? Visualization { get; set; }
}

public sealed class ReportResult
{
    public List<ReportColumn> Columns { get; set; } = [];
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public Dictionary<string, object?>? Totals { get; set; }
    public bool Truncated { get; set; }
    public int RowCount { get; set; }
    public long ExecutionMs { get; set; }
    public ReportVisualization? Visualization { get; set; }
}

public sealed class ReportFieldDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "Text";
    public bool Groupable { get; set; }
    public bool Aggregatable { get; set; }
}

public sealed class ReportDataSourceDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string DefaultDateField { get; set; } = string.Empty;
    public List<ReportFieldDto> Fields { get; set; } = [];
}

public sealed class ReportDefinitionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public string Source { get; set; } = string.Empty;
    public ReportType ReportType { get; set; }
    public bool IsSystem { get; set; }
    public string? SystemKey { get; set; }
    public ReportVisibility Visibility { get; set; }
    public Guid CreatedById { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public int RunCount { get; set; }
    public bool IsFavorite { get; set; }
    public ReportQuery Query { get; set; } = new();
}

public sealed class SaveReportDefinitionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? FolderId { get; set; }
    public ReportVisibility Visibility { get; set; } = ReportVisibility.Private;
    public ReportQuery Query { get; set; } = new();
}

public sealed class ReportFolderDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int ReportCount { get; set; }
}

public sealed class SaveReportFolderRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}

public sealed class ReportScheduleDto
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public string CronExpression { get; set; } = "0 8 * * 1"; // Weekly Monday 8am
    public string Format { get; set; } = "csv";
    public List<string> Recipients { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}

public sealed class SaveReportScheduleRequest
{
    public string CronExpression { get; set; } = "0 8 * * 1";
    public string Format { get; set; } = "csv";
    public List<string> Recipients { get; set; } = [];
    public bool IsActive { get; set; } = true;
}

public sealed class ReportRunRequest
{
    public Dictionary<string, string>? Parameters { get; set; }
    public FilterGroup? FilterOverride { get; set; }
    public ReportDateFilter? DateFilterOverride { get; set; }
    public int? Limit { get; set; }
}

public sealed class DrillDownRequest
{
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, string> GroupValues { get; set; } = [];
    public ReportDateFilter? DateFilter { get; set; }
    public FilterGroup? BaseFilter { get; set; }
}
