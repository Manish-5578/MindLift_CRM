using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MindLift.Infrastructure.Currencies;

public interface IFxRateProvider
{
    Task<(string ProviderName, IReadOnlyDictionary<string, decimal> Rates)> FetchRatesToBaseAsync(
        string baseCurrency,
        IEnumerable<string> targetCurrencies,
        string? requestedProvider,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Phase 3 / #16: Live Exchange Rate Sync (fx-sync).
/// Live provider with resilient cross-rate market fallback.
/// Connects to open exchange rate services with fallback to benchmark market rates.
/// </summary>
public sealed class LiveFxRateProvider(
    HttpClient httpClient,
    ILogger<LiveFxRateProvider> logger) : IFxRateProvider
{
    // Benchmark market rates relative to USD (1 Target = X USD)
    private static readonly Dictionary<string, decimal> UsdRateToBase = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1.00000000m,
        ["EUR"] = 1.08500000m,    // 1 EUR = 1.085 USD
        ["GBP"] = 1.28500000m,    // 1 GBP = 1.285 USD
        ["AED"] = 0.27229400m,    // 1 AED = 0.272294 USD (3.6725 AED = 1 USD)
        ["SAR"] = 0.26666700m,    // 1 SAR = 0.266667 USD (3.75 SAR = 1 USD)
        ["CAD"] = 0.73500000m,    // 1 CAD = 0.735 USD
        ["AUD"] = 0.65500000m,    // 1 AUD = 0.655 USD
        ["JPY"] = 0.00645000m,    // 1 JPY = 0.00645 USD
        ["CHF"] = 1.11500000m,    // 1 CHF = 1.115 USD
        ["INR"] = 0.01195000m,    // 1 INR = 0.01195 USD
        ["CNY"] = 0.13800000m,    // 1 CNY = 0.138 USD
        ["SGD"] = 0.74200000m,    // 1 SGD = 0.742 USD
        ["KWD"] = 3.25000000m,    // 1 KWD = 3.25 USD
        ["QAR"] = 0.27470000m,    // 1 QAR = 0.2747 USD
        ["BHD"] = 2.65250000m,    // 1 BHD = 2.6525 USD
        ["OMR"] = 2.59740000m     // 1 OMR = 2.5974 USD
    };

    public async Task<(string ProviderName, IReadOnlyDictionary<string, decimal> Rates)> FetchRatesToBaseAsync(
        string baseCurrency,
        IEnumerable<string> targetCurrencies,
        string? requestedProvider,
        CancellationToken cancellationToken = default)
    {
        var normBase = baseCurrency.Trim().ToUpperInvariant();
        var targets = targetCurrencies.Select(t => t.Trim().ToUpperInvariant()).Distinct().ToList();

        // 1. Try public live API if not explicitly requesting fallback
        if (!string.Equals(requestedProvider, "fallback", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(3));

                var url = $"https://open.er-api.com/v6/latest/{normBase}";
                var response = await httpClient.GetFromJsonAsync<OpenErApiResponse>(url, cts.Token);

                if (response is not null && string.Equals(response.Result, "success", StringComparison.OrdinalIgnoreCase) && response.Rates is not null)
                {
                    var liveRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                    foreach (var target in targets)
                    {
                        if (string.Equals(target, normBase, StringComparison.OrdinalIgnoreCase))
                        {
                            liveRates[target] = 1.0m;
                            continue;
                        }

                        if (response.Rates.TryGetValue(target, out var unitsPerBase) && unitsPerBase > 0)
                        {
                            // In open.er-api: unitsPerBase = Target per 1 Base
                            // RateToBase = Base per 1 Target = 1 / unitsPerBase
                            var rateToBase = Math.Round(1.0m / unitsPerBase, 8);
                            liveRates[target] = rateToBase;
                        }
                    }

                    if (liveRates.Count > 0)
                    {
                        var providerLabel = string.IsNullOrWhiteSpace(requestedProvider) ? "OpenExchangeRates" : requestedProvider;
                        return (providerLabel, liveRates);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Live FX provider query failed; falling back to calibrated reference rates.");
            }
        }

        // 2. Calibrated cross-rate fallback
        var fallbackRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var baseToUsd = UsdRateToBase.GetValueOrDefault(normBase, 1.0m);

        foreach (var target in targets)
        {
            if (string.Equals(target, normBase, StringComparison.OrdinalIgnoreCase))
            {
                fallbackRates[target] = 1.0m;
                continue;
            }

            var targetToUsd = UsdRateToBase.GetValueOrDefault(target, 1.0m);
            // RateToBase(Target) = Target in Base = (Target in USD) / (Base in USD)
            var rateToBase = Math.Round(targetToUsd / baseToUsd, 8);
            fallbackRates[target] = rateToBase;
        }

        return ("BenchmarkMarketRates", fallbackRates);
    }

    private sealed record OpenErApiResponse(
        string? Result,
        string? BaseCode,
        Dictionary<string, decimal>? Rates);
}
