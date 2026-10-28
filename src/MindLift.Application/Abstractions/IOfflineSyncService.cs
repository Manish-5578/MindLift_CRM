using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IOfflineSyncService
{
    Task<PwaManifestDto> GetPwaManifestAsync(CancellationToken ct = default);
    Task<OfflineBootstrapDto> GetBootstrapAsync(CancellationToken ct = default);
    Task<OfflineDeltaDto> GetDeltaAsync(DateTime sinceUtc, CancellationToken ct = default);
    Task<OfflineSyncBatchResultDto> ProcessBatchSyncAsync(OfflineSyncBatchRequest request, CancellationToken ct = default);
}
