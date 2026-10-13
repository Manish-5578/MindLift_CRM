using System.Text.Json;
using MindLift.Domain.Common;
using MindLift.Shared.Contracts;

namespace MindLift.Domain.Entities;

/// <summary>
/// Organizational folder for grouping reports.
/// </summary>
[Audited]
public sealed class ReportFolder : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public ReportFolder? Parent { get; set; }
    public ICollection<ReportFolder> Children { get; set; } = [];
    public ICollection<ReportDefinition> Reports { get; set; } = [];
}

/// <summary>
/// Custom or system report definition storing query specification, metadata, and visibility.
/// </summary>
[Audited]
public sealed class ReportDefinition : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? FolderId { get; set; }
    public ReportFolder? Folder { get; set; }

    public string Source { get; set; } = string.Empty;
    public ReportType ReportType { get; set; }
    public bool IsSystem { get; set; }
    public string? SystemKey { get; set; }
    public ReportVisibility Visibility { get; set; } = ReportVisibility.Private;

    public JsonDocument QueryJson { get; set; } = JsonDocument.Parse("{}");

    public DateTime? LastRunAt { get; set; }
    public int RunCount { get; set; }

    public ICollection<ReportFavorite> Favorites { get; set; } = [];
    public ICollection<ReportSchedule> Schedules { get; set; } = [];
}

/// <summary>
/// Starred / favorite reports per user.
/// </summary>
[Audited]
public sealed class ReportFavorite : TenantEntity
{
    public Guid ReportId { get; set; }
    public ReportDefinition? Report { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>
/// Scheduled delivery of a report via email or notification.
/// </summary>
[Audited]
public sealed class ReportSchedule : TenantEntity
{
    public Guid ReportId { get; set; }
    public ReportDefinition? Report { get; set; }

    public string CronExpression { get; set; } = "0 8 * * 1";
    public string Format { get; set; } = "csv";
    public JsonDocument RecipientsJson { get; set; } = JsonDocument.Parse("[]");
    public bool IsActive { get; set; } = true;

    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}

/// <summary>
/// Audit trail of report executions and exports.
/// </summary>
[Audited]
public sealed class ReportExecutionLog : TenantEntity
{
    public Guid? ReportId { get; set; }
    public string ReportName { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public long ExecutionMs { get; set; }
    public int RowCount { get; set; }
    public string? ExportFormat { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}
