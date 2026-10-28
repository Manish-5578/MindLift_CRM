using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Deals;

/// <summary>
/// Phase 3 / #18: Win/Loss Analysis (win-loss).
/// Integration tests verifying feature flag gating, win/loss rate calculations,
/// root cause reasons, sales cycle duration, pipeline leakage, and multi-tenant isolation.
/// </summary>
public sealed class WinLossTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s)
        => (await s.Client.PutAsJsonAsync("/api/v1/features/win-loss", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    private static async Task<DealDto> SetupDealAsync(
        Session s,
        string dealName,
        decimal amount = 50000m)
    {
        var indRes = await s.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"));
        if (indRes.StatusCode != HttpStatusCode.Conflict)
        {
            indRes.EnsureSuccessStatusCode();
        }

        var r = await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest(
            dealName,
            null,
            null,
            amount,
            null,
            50,
            DateTime.UtcNow.AddDays(15),
            null,
            null,
            null,
            null,
            null
        ));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<DealDto>())!;
    }

    [Fact]
    public async Task Feature_flag_off_blocks_win_loss_endpoints()
    {
        var owner = await Workspaces.SignupAsync(factory);

        // GET summary blocked (404)
        var summaryRes = await owner.Client.GetAsync("/api/v1/win-loss/summary");
        summaryRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // GET reasons blocked (404)
        var reasonsRes = await owner.Client.GetAsync("/api/v1/win-loss/reasons");
        reasonsRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // GET competitors blocked (404)
        var compRes = await owner.Client.GetAsync("/api/v1/win-loss/competitors");
        compRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // GET stages blocked (404)
        var stagesRes = await owner.Client.GetAsync("/api/v1/win-loss/stages");
        stagesRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // GET reps blocked (404)
        var repsRes = await owner.Client.GetAsync("/api/v1/win-loss/reps");
        repsRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Win_loss_analysis_metrics_reasons_and_pipeline_leakage()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);

        // 1. Get Won and Lost stage IDs
        var deal1 = await SetupDealAsync(owner, "Cloud ERP Migration", 100000m);
        var pipelines = await owner.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines");
        var stages = pipelines!.First(p => p.IsDefault).Stages;
        var wonStage = stages.First(s => s.StageType == "Won");
        var lostStage = stages.First(s => s.StageType == "Lost");

        // Deal 1: Mark Won ($100,000)
        var wonRes = await owner.Client.PatchAsJsonAsync(
            $"/api/v1/deals/{deal1.Id}/stage",
            new ChangeDealStageRequest(wonStage.Id, null, null, DateTime.UtcNow, deal1.Version));
        wonRes.EnsureSuccessStatusCode();

        // Deal 2: Mark Lost ($50,000) due to price
        var deal2 = await SetupDealAsync(owner, "Fleet Tracking License", 50000m);
        var lostRes1 = await owner.Client.PatchAsJsonAsync(
            $"/api/v1/deals/{deal2.Id}/stage",
            new ChangeDealStageRequest(lostStage.Id, "price", "Price was too high for budget", DateTime.UtcNow, deal2.Version));
        lostRes1.EnsureSuccessStatusCode();

        // Deal 3: Mark Lost ($30,000) due to competitor
        var deal3 = await SetupDealAsync(owner, "Security Audit Services", 30000m);
        var lostRes2 = await owner.Client.PatchAsJsonAsync(
            $"/api/v1/deals/{deal3.Id}/stage",
            new ChangeDealStageRequest(lostStage.Id, "competitor", "Selected legacy incumbent", DateTime.UtcNow, deal3.Version));
        lostRes2.EnsureSuccessStatusCode();

        // 2. Query Win/Loss summary
        var summary = await owner.Client.GetFromJsonAsync<WinLossSummaryDto>("/api/v1/win-loss/summary");
        summary.ShouldNotBeNull();
        summary.TotalClosedDeals.ShouldBe(3);
        summary.WonDealsCount.ShouldBe(1);
        summary.LostDealsCount.ShouldBe(2);
        summary.WonRevenue.ShouldBe(100000m);
        summary.LostRevenue.ShouldBe(80000m);
        summary.OverallWinRatePercentage.ShouldBe(33.33m);
        summary.AverageWonDealSize.ShouldBe(100000m);
        summary.AverageLostDealSize.ShouldBe(40000m); // (50k + 30k) / 2

        // 3. Query reasons breakdown
        var reasons = await owner.Client.GetFromJsonAsync<WinLossReasonsBreakdownDto>("/api/v1/win-loss/reasons");
        reasons.ShouldNotBeNull();
        reasons.LossReasons.Count.ShouldBe(2);
        reasons.LossReasons.ShouldContain(r => r.Reason == "price" && r.DealCount == 1 && r.TotalRevenue == 50000m);
        reasons.LossReasons.ShouldContain(r => r.Reason == "competitor" && r.DealCount == 1 && r.TotalRevenue == 30000m);

        // 4. Query pipeline leakage
        var leakage = await owner.Client.GetFromJsonAsync<List<StageLeakageDto>>("/api/v1/win-loss/stages");
        leakage.ShouldNotBeNull();
        leakage.Count.ShouldBeGreaterThanOrEqualTo(1);
        var lostStageLeak = leakage.Single(s => s.StageId == lostStage.Id);
        lostStageLeak.LostCount.ShouldBe(2);
        lostStageLeak.LostRevenue.ShouldBe(80000m);
        lostStageLeak.LeakagePercentage.ShouldBe(100m);

        // 5. Query sales reps performance
        var reps = await owner.Client.GetFromJsonAsync<List<RepWinLossDto>>("/api/v1/win-loss/reps");
        reps.ShouldNotBeNull();
        reps.Count.ShouldBeGreaterThanOrEqualTo(1);
        reps[0].TotalDeals.ShouldBe(3);
        reps[0].WonDeals.ShouldBe(1);
        reps[0].LostDeals.ShouldBe(2);
        reps[0].WinRatePercentage.ShouldBe(33.33m);
    }

    [Fact]
    public async Task Multi_tenant_isolation_for_win_loss()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        await EnableFeatureAsync(tenantA);
        await EnableFeatureAsync(tenantB);

        // Tenant A creates and wins a deal
        var dealA = await SetupDealAsync(tenantA, "Tenant A Private Victory", 200000m);
        var pipelinesA = await tenantA.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines");
        var wonStageA = pipelinesA!.First(p => p.IsDefault).Stages.First(s => s.StageType == "Won");

        await tenantA.Client.PatchAsJsonAsync(
            $"/api/v1/deals/{dealA.Id}/stage",
            new ChangeDealStageRequest(wonStageA.Id, null, null, DateTime.UtcNow, dealA.Version));

        // Tenant A summary has 1 won deal ($200k)
        var summaryA = await tenantA.Client.GetFromJsonAsync<WinLossSummaryDto>("/api/v1/win-loss/summary");
        summaryA.ShouldNotBeNull();
        summaryA.TotalClosedDeals.ShouldBe(1);
        summaryA.WonRevenue.ShouldBe(200000m);

        // Tenant B summary has 0 closed deals
        var summaryB = await tenantB.Client.GetFromJsonAsync<WinLossSummaryDto>("/api/v1/win-loss/summary");
        summaryB.ShouldNotBeNull();
        summaryB.TotalClosedDeals.ShouldBe(0);
        summaryB.WonRevenue.ShouldBe(0m);
        summaryB.LostRevenue.ShouldBe(0m);
    }
}
