using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Currencies;

/// <summary>
/// Phase 3 / #16: Live Exchange Rate Sync (fx-sync).
/// Tests feature flag gating, automated exchange rate sync against live/fallback providers,
/// audit logging of sync operations, multi-currency conversion calculations, and multi-tenant isolation.
/// </summary>
public sealed class FxSyncTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s)
        => (await s.Client.PutAsJsonAsync("/api/v1/features/fx-sync", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    [Fact]
    public async Task Feature_flag_off_blocks_fx_sync_endpoints()
    {
        var owner = await Workspaces.SignupAsync(factory);

        // POST sync blocked (404)
        var syncRes = await owner.Client.PostAsJsonAsync("/api/v1/fx/sync", new FxSyncRequest());
        syncRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // GET status blocked (404)
        var statusRes = await owner.Client.GetAsync("/api/v1/fx/status");
        statusRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // GET rates blocked (404)
        var ratesRes = await owner.Client.GetAsync("/api/v1/fx/rates");
        ratesRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // POST convert blocked (404)
        var convertRes = await owner.Client.PostAsJsonAsync("/api/v1/fx/convert", new FxConvertRequest(100m, "USD", "EUR"));
        convertRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Fx_sync_fetches_and_persists_exchange_rates_with_audit_log()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);

        // 1. Initial status
        var initialStatus = await owner.Client.GetFromJsonAsync<FxSyncStatusDto>("/api/v1/fx/status");
        initialStatus.ShouldNotBeNull();
        initialStatus.BaseCurrency.ShouldNotBeNullOrWhiteSpace();

        // 2. Enable EUR and AED in tenant currency settings
        var currRes = await owner.Client.PutAsJsonAsync("/api/v1/settings/currencies", new SaveCurrencySettingsRequest("USD", ["EUR", "AED"]));
        currRes.EnsureSuccessStatusCode();

        // 3. Trigger live exchange rate sync
        var syncRes = await owner.Client.PostAsJsonAsync("/api/v1/fx/sync", new FxSyncRequest());
        syncRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var syncResult = await syncRes.Content.ReadFromJsonAsync<FxSyncResultDto>();
        syncResult.ShouldNotBeNull();
        syncResult.Success.ShouldBeTrue();
        syncResult.BaseCurrency.ShouldBe("USD");
        syncResult.SyncedCount.ShouldBeGreaterThanOrEqualTo(2);
        syncResult.Rates.ShouldContain(r => r.Currency == "EUR" && r.RateToBase > 0);
        syncResult.Rates.ShouldContain(r => r.Currency == "AED" && r.RateToBase > 0);

        // 4. Verify sync status endpoint reflects latest audit log
        var status = await owner.Client.GetFromJsonAsync<FxSyncStatusDto>("/api/v1/fx/status");
        status.ShouldNotBeNull();
        status.LastSyncedAtUtc.ShouldNotBeNull();
        status.LastStatus.ShouldBe("Success");
        status.LastProvider.ShouldNotBeNull();
        status.ActiveRates.Count.ShouldBeGreaterThanOrEqualTo(2);

        // 5. Verify GET rates endpoint
        var rates = await owner.Client.GetFromJsonAsync<List<FxRateDto>>("/api/v1/fx/rates");
        rates.ShouldNotBeNull();
        rates.ShouldContain(r => r.Currency == "EUR");
        rates.ShouldContain(r => r.Currency == "AED");
    }

    [Fact]
    public async Task Fx_convert_performs_accurate_multi_currency_conversions()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);

        // Enable EUR and AED
        await owner.Client.PutAsJsonAsync("/api/v1/settings/currencies", new SaveCurrencySettingsRequest("USD", ["EUR", "AED"]));
        // Sync rates
        await owner.Client.PostAsJsonAsync("/api/v1/fx/sync", new FxSyncRequest());

        // 1. Same currency conversion (e.g. 100 USD to USD)
        var sameRes = await owner.Client.PostAsJsonAsync("/api/v1/fx/convert", new FxConvertRequest(100m, "USD", "USD"));
        sameRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var same = await sameRes.Content.ReadFromJsonAsync<FxConvertResultDto>();
        same.ShouldNotBeNull();
        same.ConvertedAmount.ShouldBe(100m);
        same.ExchangeRateUsed.ShouldBe(1.0m);

        // 2. Foreign currency to Base (e.g. 100 EUR to USD)
        var eurRes = await owner.Client.PostAsJsonAsync("/api/v1/fx/convert", new FxConvertRequest(100m, "EUR", "USD"));
        eurRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eurToUsd = await eurRes.Content.ReadFromJsonAsync<FxConvertResultDto>();
        eurToUsd.ShouldNotBeNull();
        eurToUsd.ConvertedAmount.ShouldBeGreaterThan(50m);
        eurToUsd.ExchangeRateUsed.ShouldBeGreaterThan(0.5m);

        // 3. Cross currency conversion (e.g. 1000 AED to EUR)
        var crossRes = await owner.Client.PostAsJsonAsync("/api/v1/fx/convert", new FxConvertRequest(1000m, "AED", "EUR"));
        crossRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var aedToEur = await crossRes.Content.ReadFromJsonAsync<FxConvertResultDto>();
        aedToEur.ShouldNotBeNull();
        aedToEur.ConvertedAmount.ShouldBeGreaterThan(100m);
        aedToEur.ExchangeRateUsed.ShouldBeGreaterThan(0.1m);

        // 4. Invalid currency conversion returns 400
        var invalidRes = await owner.Client.PostAsJsonAsync("/api/v1/fx/convert", new FxConvertRequest(50m, "NONEXISTENT", "USD"));
        invalidRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Multi_tenant_isolation_for_fx_sync()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        await EnableFeatureAsync(tenantA);
        await EnableFeatureAsync(tenantB);

        // Tenant A enables GBP and syncs
        await tenantA.Client.PutAsJsonAsync("/api/v1/settings/currencies", new SaveCurrencySettingsRequest("USD", ["GBP"]));
        await tenantA.Client.PostAsJsonAsync("/api/v1/fx/sync", new FxSyncRequest());

        // Tenant A has GBP rate
        var ratesA = await tenantA.Client.GetFromJsonAsync<List<FxRateDto>>("/api/v1/fx/rates");
        ratesA.ShouldNotBeNull();
        ratesA.ShouldContain(r => r.Currency == "GBP");

        // Tenant B has NOT enabled GBP -> rates list does NOT contain GBP
        var ratesB = await tenantB.Client.GetFromJsonAsync<List<FxRateDto>>("/api/v1/fx/rates");
        ratesB.ShouldNotBeNull();
        ratesB.ShouldNotContain(r => r.Currency == "GBP");

        // Tenant B cannot convert GBP since it is not in Tenant B's rates
        var convertB = await tenantB.Client.PostAsJsonAsync("/api/v1/fx/convert", new FxConvertRequest(100m, "GBP", "USD"));
        convertB.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
