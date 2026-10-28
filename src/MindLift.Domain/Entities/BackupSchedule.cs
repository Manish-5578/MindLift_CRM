using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 2 / #13: Scheduled Backups (scheduled-backups).
/// Per-tenant backup configuration for recurring automated data exports.
/// </summary>
[Audited]
public sealed class BackupScheduleConfig : TenantEntity
{
    public bool IsEnabled { get; set; } = true;

    /// <summary>"Weekly", "Monthly", or "Daily".</summary>
    public string Frequency { get; set; } = "Weekly";

    public DayOfWeek? DayOfWeek { get; set; } = System.DayOfWeek.Sunday;

    public int? DayOfMonth { get; set; }

    /// <summary>Hour in UTC (0-23) when scheduled backup should trigger.</summary>
    public int HourUtc { get; set; } = 2;

    /// <summary>Number of days backup archives are retained before expiry (default 30 days).</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Default export format: "json" or "csv".</summary>
    public string Format { get; set; } = "json";

    /// <summary>Notification email addresses for completed backups.</summary>
    public string[] NotifyEmails { get; set; } = [];

    public DateTime? LastRunAt { get; set; }

    public DateTime? NextRunAt { get; set; }
}

/// <summary>
/// Phase 2 / #13: Scheduled Backups (scheduled-backups).
/// Generated backup archive record containing checksums, metadata, and export data.
/// </summary>
[Audited]
public sealed class BackupArchiveRecord : TenantEntity
{
    /// <summary>"Pending", "Completed", or "Failed".</summary>
    public string Status { get; set; } = "Completed";

    public string Format { get; set; } = "json";

    public long SizeBytes { get; set; }

    public string ChecksumSha256 { get; set; } = string.Empty;

    /// <summary>JSON object with count of exported entities (e.g. {"leads": 50, "contacts": 120}).</summary>
    public string RecordCountsJson { get; set; } = "{}";

    /// <summary>Serialized payload contents for download.</summary>
    public string? PayloadContent { get; set; }

    public DateTime ExpiresAt { get; set; }

    public string? ErrorMessage { get; set; }

    public string DownloadToken { get; set; } = Guid.NewGuid().ToString("N");

    public int DownloadCount { get; set; }
}
