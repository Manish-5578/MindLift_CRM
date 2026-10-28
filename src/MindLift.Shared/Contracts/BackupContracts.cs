namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 2 / #13: Scheduled Backups (scheduled-backups).
/// Contracts for backup schedule configuration, archive listings, and on-demand triggers.
/// </summary>
public sealed record BackupScheduleConfigDto(
    Guid Id,
    bool IsEnabled,
    string Frequency,
    DayOfWeek? DayOfWeek,
    int? DayOfMonth,
    int HourUtc,
    int RetentionDays,
    string Format,
    IReadOnlyList<string> NotifyEmails,
    DateTime? LastRunAt,
    DateTime? NextRunAt);

public sealed record SaveBackupScheduleConfigRequest(
    bool IsEnabled,
    string Frequency,
    DayOfWeek? DayOfWeek = null,
    int? DayOfMonth = null,
    int HourUtc = 2,
    int RetentionDays = 30,
    string Format = "json",
    IReadOnlyList<string>? NotifyEmails = null);

public sealed record BackupArchiveDto(
    Guid Id,
    string Status,
    string Format,
    long SizeBytes,
    string ChecksumSha256,
    IReadOnlyDictionary<string, int> RecordCounts,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    int DownloadCount,
    string? ErrorMessage);

public sealed record TriggerBackupRequest(
    string? Format = "json");

public sealed record BackupDownloadResult(
    string FileName,
    string ContentType,
    byte[] Data);
