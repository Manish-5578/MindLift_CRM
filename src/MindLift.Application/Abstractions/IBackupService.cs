using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 2 / #13: Scheduled Backups (scheduled-backups).
/// Service abstraction for scheduling, generating, and downloading tenant backup archives.
/// </summary>
public interface IBackupService
{
    Task<BackupScheduleConfigDto> GetScheduleConfigAsync(CancellationToken cancellationToken = default);

    Task<BackupScheduleConfigDto> SaveScheduleConfigAsync(SaveBackupScheduleConfigRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupArchiveDto>> ListArchivesAsync(CancellationToken cancellationToken = default);

    Task<BackupArchiveDto> GetArchiveAsync(Guid id, CancellationToken cancellationToken = default);

    Task<BackupArchiveDto> TriggerBackupAsync(TriggerBackupRequest? request = null, CancellationToken cancellationToken = default);

    Task<BackupDownloadResult> DownloadArchiveAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteArchiveAsync(Guid id, CancellationToken cancellationToken = default);
}
