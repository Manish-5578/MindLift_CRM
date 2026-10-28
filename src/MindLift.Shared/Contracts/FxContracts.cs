namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 3 / #16: Live Exchange Rate Sync (fx-sync).
/// Contracts for foreign exchange synchronization, conversion, and audit logging.
/// </summary>
public sealed record FxRateDto(
    string Currency,
    decimal RateToBase,
    DateTime EffectiveDateUtc,
    string Source = "Live");

public sealed record FxSyncRequest(
    string? Provider = null);

public sealed record FxSyncResultDto(
    bool Success,
    string Provider,
    string BaseCurrency,
    int SyncedCount,
    DateTime SyncedAtUtc,
    string? Message,
    IReadOnlyList<FxRateDto> Rates);

public sealed record FxSyncStatusDto(
    string BaseCurrency,
    IReadOnlyList<string> EnabledCurrencies,
    DateTime? LastSyncedAtUtc,
    string? LastProvider,
    string? LastStatus,
    string? LastError,
    IReadOnlyList<FxRateDto> ActiveRates);

public sealed record FxConvertRequest(
    decimal Amount,
    string FromCurrency,
    string ToCurrency);

public sealed record FxConvertResultDto(
    decimal OriginalAmount,
    string FromCurrency,
    string ToCurrency,
    decimal ConvertedAmount,
    decimal ExchangeRateUsed,
    DateTime AsOfUtc);
