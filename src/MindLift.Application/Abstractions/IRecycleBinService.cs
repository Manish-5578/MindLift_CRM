using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 2 / #8: Recycle Bin (recycle-bin).
/// Provides listing, restoring, and purging of soft-deleted records within the 60-day retention window.
/// </summary>
public interface IRecycleBinService
{
    Task<RecycleBinPagedResult> ListAsync(RecycleBinQuery query, CancellationToken cancellationToken = default);
    Task<RecycleBinOperationResult> RestoreAsync(RestoreRecordsRequest request, CancellationToken cancellationToken = default);
    Task<RecycleBinOperationResult> PurgeAsync(PurgeRecordsRequest request, CancellationToken cancellationToken = default);
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}
