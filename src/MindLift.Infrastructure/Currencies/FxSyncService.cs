using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Settings;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Currencies;

/// <summary>
/// Phase 3 / #16: Live Exchange Rate Sync (fx-sync).
/// Service for fetching live exchange rates, automated/on-demand synchronization,
/// multi-currency conversions, and audit logging.
/// </summary>
public sealed class FxSyncService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    SettingsVersion version,
    IFxRateProvider provider,
    ILogger<FxSyncService> logger) : IFxSyncService
{
    public async Task<FxSyncResultDto> SyncRatesAsync(FxSyncRequest? request = null, CancellationToken cancellationToken = default)
    {
        var settings = await db.TenantSettings.FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("TenantSettings", tenant.TenantId);

        var baseCurrency = settings.BaseCurrency.Trim().ToUpperInvariant();
        var enabledTargets = settings.EnabledCurrencies
            .Select(c => c.Trim().ToUpperInvariant())
            .Where(c => !string.IsNullOrWhiteSpace(c) && c != baseCurrency)
            .Distinct()
            .ToList();

        var now = clock.UtcNow;

        if (enabledTargets.Count == 0)
        {
            var logEmpty = new FxSyncLog
            {
                Provider = request?.Provider ?? "None",
                BaseCurrency = baseCurrency,
                CurrenciesSynced = 0,
                Status = "Success",
                SyncedAt = now
            };
            db.FxSyncLogs.Add(logEmpty);
            await db.SaveChangesAsync(cancellationToken);

            return new FxSyncResultDto(
                Success: true,
                Provider: logEmpty.Provider,
                BaseCurrency: baseCurrency,
                SyncedCount: 0,
                SyncedAtUtc: now,
                Message: "No additional currencies enabled to sync.",
                Rates: []
            );
        }

        try
        {
            var (providerName, rates) = await provider.FetchRatesToBaseAsync(
                baseCurrency,
                enabledTargets,
                request?.Provider,
                cancellationToken);

            var resultDtos = new List<FxRateDto>();

            foreach (var (currency, rateToBase) in rates)
            {
                if (rateToBase <= 0) continue;

                var existing = await db.ExchangeRates
                    .FirstOrDefaultAsync(r => r.Currency == currency && r.EffectiveDate.Date == now.Date, cancellationToken);

                if (existing is not null)
                {
                    existing.RateToBase = rateToBase;
                    existing.EffectiveDate = now;
                }
                else
                {
                    db.ExchangeRates.Add(new ExchangeRate
                    {
                        Currency = currency,
                        RateToBase = rateToBase,
                        EffectiveDate = now
                    });
                }

                resultDtos.Add(new FxRateDto(currency, rateToBase, now, providerName));
            }

            var syncLog = new FxSyncLog
            {
                Provider = providerName,
                BaseCurrency = baseCurrency,
                CurrenciesSynced = resultDtos.Count,
                Status = "Success",
                SyncedAt = now
            };
            db.FxSyncLogs.Add(syncLog);

            await db.SaveChangesAsync(cancellationToken);
            await version.BumpAsync(cancellationToken);

            return new FxSyncResultDto(
                Success: true,
                Provider: providerName,
                BaseCurrency: baseCurrency,
                SyncedCount: resultDtos.Count,
                SyncedAtUtc: now,
                Message: $"Successfully synchronized {resultDtos.Count} currency rates from {providerName}.",
                Rates: resultDtos
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exchange rate sync failed for base currency {BaseCurrency}", baseCurrency);

            var failLog = new FxSyncLog
            {
                Provider = request?.Provider ?? "Unknown",
                BaseCurrency = baseCurrency,
                CurrenciesSynced = 0,
                Status = "Failed",
                ErrorMessage = ex.Message,
                SyncedAt = now
            };
            db.FxSyncLogs.Add(failLog);
            await db.SaveChangesAsync(cancellationToken);

            return new FxSyncResultDto(
                Success: false,
                Provider: failLog.Provider,
                BaseCurrency: baseCurrency,
                SyncedCount: 0,
                SyncedAtUtc: now,
                Message: $"Sync failed: {ex.Message}",
                Rates: []
            );
        }
    }

    public async Task<FxSyncStatusDto> GetSyncStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("TenantSettings", tenant.TenantId);

        var lastLog = await db.FxSyncLogs.AsNoTracking()
            .OrderByDescending(l => l.SyncedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var activeRates = await GetRatesAsync(cancellationToken);

        return new FxSyncStatusDto(
            BaseCurrency: settings.BaseCurrency,
            EnabledCurrencies: settings.EnabledCurrencies,
            LastSyncedAtUtc: lastLog?.SyncedAt,
            LastProvider: lastLog?.Provider,
            LastStatus: lastLog?.Status,
            LastError: lastLog?.ErrorMessage,
            ActiveRates: activeRates
        );
    }

    public async Task<FxConvertResultDto> ConvertCurrencyAsync(FxConvertRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount < 0)
            throw new ValidationException("Amount", "Conversion amount cannot be negative.");

        var from = request.FromCurrency?.Trim().ToUpperInvariant();
        var to = request.ToCurrency?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(from))
            throw new ValidationException("FromCurrency", "FromCurrency is required.");
        if (string.IsNullOrWhiteSpace(to))
            throw new ValidationException("ToCurrency", "ToCurrency is required.");

        var now = clock.UtcNow;

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return new FxConvertResultDto(
                OriginalAmount: request.Amount,
                FromCurrency: from,
                ToCurrency: to,
                ConvertedAmount: request.Amount,
                ExchangeRateUsed: 1.00000000m,
                AsOfUtc: now
            );
        }

        var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("TenantSettings", tenant.TenantId);

        var baseCurrency = settings.BaseCurrency.Trim().ToUpperInvariant();
        var rates = await GetLatestRatesDictionaryAsync(baseCurrency, cancellationToken);

        var fromRateToBase = string.Equals(from, baseCurrency, StringComparison.OrdinalIgnoreCase)
            ? 1.0m
            : rates.GetValueOrDefault(from, 0m);

        var toRateToBase = string.Equals(to, baseCurrency, StringComparison.OrdinalIgnoreCase)
            ? 1.0m
            : rates.GetValueOrDefault(to, 0m);

        if (fromRateToBase <= 0)
            throw new ValidationException("FromCurrency", $"No active exchange rate found for '{from}'. Please synchronize exchange rates.");

        if (toRateToBase <= 0)
            throw new ValidationException("ToCurrency", $"No active exchange rate found for '{to}'. Please synchronize exchange rates.");

        var amountInBase = request.Amount * fromRateToBase;
        var convertedAmount = Math.Round(amountInBase / toRateToBase, 4);
        var effectiveRate = Math.Round(fromRateToBase / toRateToBase, 8);

        return new FxConvertResultDto(
            OriginalAmount: request.Amount,
            FromCurrency: from,
            ToCurrency: to,
            ConvertedAmount: convertedAmount,
            ExchangeRateUsed: effectiveRate,
            AsOfUtc: now
        );
    }

    public async Task<IReadOnlyList<FxRateDto>> GetRatesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.ExchangeRates.AsNoTracking()
            .OrderByDescending(r => r.EffectiveDate)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.Currency, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var newest = g.First();
                return new FxRateDto(newest.Currency, newest.RateToBase, newest.EffectiveDate);
            })
            .OrderBy(r => r.Currency)
            .ToList();
    }

    private async Task<Dictionary<string, decimal>> GetLatestRatesDictionaryAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        var rows = await db.ExchangeRates.AsNoTracking()
            .OrderBy(r => r.EffectiveDate)
            .ToListAsync(cancellationToken);

        var dict = rows.GroupBy(r => r.Currency, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().RateToBase, StringComparer.OrdinalIgnoreCase);

        dict[baseCurrency] = 1.0m;
        return dict;
    }
}
