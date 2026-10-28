using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 3 / #16: Live Exchange Rate Sync (fx-sync).
/// Service abstraction for synchronizing foreign exchange rates, currency conversion, and sync status tracking.
/// </summary>
public interface IFxSyncService
{
    Task<FxSyncResultDto> SyncRatesAsync(FxSyncRequest? request = null, CancellationToken cancellationToken = default);

    Task<FxSyncStatusDto> GetSyncStatusAsync(CancellationToken cancellationToken = default);

    Task<FxConvertResultDto> ConvertCurrencyAsync(FxConvertRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FxRateDto>> GetRatesAsync(CancellationToken cancellationToken = default);
}
