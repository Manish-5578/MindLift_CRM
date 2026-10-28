using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Deals;

/// <summary>
/// Phase 3 / #17: Competitor Tracking (competitors).
/// Tests feature flag gating, competitor battlecards CRUD, deal-competitor associations,
/// threat levels, competitive win/loss win rate calculations, and multi-tenant isolation.
/// </summary>
public sealed class CompetitorTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s)
        => (await s.Client.PutAsJsonAsync("/api/v1/features/competitors", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

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
            DateTime.UtcNow.AddDays(20),
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
    public async Task Feature_flag_off_blocks_competitor_endpoints()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var deal = await SetupDealAsync(owner, "Shield Deal");

        // GET competitors blocked (404)
        var listRes = await owner.Client.GetAsync("/api/v1/competitors");
        listRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // POST competitor blocked (404)
        var createRes = await owner.Client.PostAsJsonAsync("/api/v1/competitors", new SaveCompetitorRequest("RivalCorp"));
        createRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // GET deal competitors blocked (404)
        var dealCompRes = await owner.Client.GetAsync($"/api/v1/deals/{deal.Id}/competitors");
        dealCompRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // GET analytics blocked (404)
        var analyticsRes = await owner.Client.GetAsync("/api/v1/competitors/analytics");
        analyticsRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Competitor_crud_battlecards_and_deal_associations()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);

        // 1. Create a competitor with battlecard info
        var saveReq = new SaveCompetitorRequest(
            Name: "Acme Cloud ERP",
            Website: "https://acme-erp.example.com",
            Strengths: "Brand name, legacy market presence",
            Weaknesses: "Slow UI, expensive implementation, rigid customization",
            Battlecard: "Emphasize MindLift modern speed, custom object flexibility, and transparent pricing.",
            PricingStrategy: "High upfront licensing + 20% annual maintenance"
        );
        var createRes = await owner.Client.PostAsJsonAsync("/api/v1/competitors", saveReq);
        createRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var comp = await createRes.Content.ReadFromJsonAsync<CompetitorDto>();
        comp.ShouldNotBeNull();
        comp.Name.ShouldBe("Acme Cloud ERP");
        comp.Strengths.ShouldBe("Brand name, legacy market presence");

        // 2. Update competitor
        var updateReq = saveReq with { Strengths = "Brand name, legacy presence, global reach" };
        var updateRes = await owner.Client.PutAsJsonAsync($"/api/v1/competitors/{comp.Id}", updateReq);
        updateRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedComp = await updateRes.Content.ReadFromJsonAsync<CompetitorDto>();
        updatedComp.ShouldNotBeNull();
        updatedComp.Strengths.ShouldBe("Brand name, legacy presence, global reach");

        // 3. Create a deal and associate the competitor
        var deal = await SetupDealAsync(owner, "Global Supply Chain Migration", 120000m);

        var addCompReq = new AddDealCompetitorRequest(
            CompetitorId: comp.Id,
            ThreatLevel: "High",
            StrengthsObserved: "Incumbent vendor in 2 branches",
            WeaknessesObserved: "Over budget on renewal quote",
            Notes: "Client seeking modern alternatives to Acme"
        );
        var addRes = await owner.Client.PostAsJsonAsync($"/api/v1/deals/{deal.Id}/competitors", addCompReq);
        addRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dealComp = await addRes.Content.ReadFromJsonAsync<DealCompetitorDto>();
        dealComp.ShouldNotBeNull();
        dealComp.CompetitorId.ShouldBe(comp.Id);
        dealComp.CompetitorName.ShouldBe("Acme Cloud ERP");
        dealComp.ThreatLevel.ShouldBe("High");

        // 4. Verify deal competitors list
        var dealComps = await owner.Client.GetFromJsonAsync<List<DealCompetitorDto>>($"/api/v1/deals/{deal.Id}/competitors");
        dealComps.ShouldNotBeNull();
        dealComps.Count.ShouldBe(1);
        dealComps[0].CompetitorId.ShouldBe(comp.Id);

        // 5. Update threat level on deal
        var updateThreatRes = await owner.Client.PutAsJsonAsync(
            $"/api/v1/deals/{deal.Id}/competitors/{comp.Id}",
            new UpdateDealCompetitorRequest("Low", Notes: "Customer decided not to renew with Acme"));
        updateThreatRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedDealComp = await updateThreatRes.Content.ReadFromJsonAsync<DealCompetitorDto>();
        updatedDealComp.ShouldNotBeNull();
        updatedDealComp.ThreatLevel.ShouldBe("Low");

        // 6. Remove competitor from deal
        var removeRes = await owner.Client.DeleteAsync($"/api/v1/deals/{deal.Id}/competitors/{comp.Id}");
        removeRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var dealCompsAfter = await owner.Client.GetFromJsonAsync<List<DealCompetitorDto>>($"/api/v1/deals/{deal.Id}/competitors");
        dealCompsAfter.ShouldNotBeNull();
        dealCompsAfter.ShouldBeEmpty();
    }

    [Fact]
    public async Task Competitive_win_rate_and_revenue_analytics()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);

        // 1. Create competitor "Apex CRM"
        var compRes = await owner.Client.PostAsJsonAsync("/api/v1/competitors", new SaveCompetitorRequest("Apex CRM"));
        compRes.EnsureSuccessStatusCode();
        var apex = (await compRes.Content.ReadFromJsonAsync<CompetitorDto>())!;

        // 2. Create Deal 1 ($100,000) with Apex and mark Won
        var deal1 = await SetupDealAsync(owner, "Fintech Deal", 100000m);
        await owner.Client.PostAsJsonAsync($"/api/v1/deals/{deal1.Id}/competitors", new AddDealCompetitorRequest(apex.Id));
        // Find Won and Lost stages in default pipeline
        var pipelines = await owner.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines");
        var stages = pipelines!.First(p => p.IsDefault).Stages;
        var wonStage = stages.First(s => s.StageType == "Won");
        var lostStage = stages.First(s => s.StageType == "Lost");

        var wonRes = await owner.Client.PatchAsJsonAsync($"/api/v1/deals/{deal1.Id}/stage", new ChangeDealStageRequest(wonStage.Id, null, null, DateTime.UtcNow, deal1.Version));
        wonRes.EnsureSuccessStatusCode();

        // 3. Create Deal 2 ($50,000) with Apex and mark Lost
        var deal2 = await SetupDealAsync(owner, "Healthcare Portal", 50000m);
        await owner.Client.PostAsJsonAsync($"/api/v1/deals/{deal2.Id}/competitors", new AddDealCompetitorRequest(apex.Id));
        var lostRes = await owner.Client.PatchAsJsonAsync($"/api/v1/deals/{deal2.Id}/stage", new ChangeDealStageRequest(lostStage.Id, "competitor", "Competitor undercut by 30%", DateTime.UtcNow, deal2.Version));
        lostRes.EnsureSuccessStatusCode();

        // 4. Create Deal 3 ($25,000) with Apex (remains Open)
        var deal3 = await SetupDealAsync(owner, "Logistics SaaS", 25000m);
        await owner.Client.PostAsJsonAsync($"/api/v1/deals/{deal3.Id}/competitors", new AddDealCompetitorRequest(apex.Id));

        // 5. Query competitor profile and verify win rate analytics
        var profile = await owner.Client.GetFromJsonAsync<CompetitorDto>($"/api/v1/competitors/{apex.Id}");
        profile.ShouldNotBeNull();
        profile.TotalDealsCount.ShouldBe(3);
        profile.WonDealsCount.ShouldBe(1);
        profile.LostDealsCount.ShouldBe(1);
        profile.WonRevenue.ShouldBe(100000m);
        profile.LostRevenue.ShouldBe(50000m);
        profile.WinRatePercentage.ShouldBe(50.00m); // 1 won out of 2 closed deals = 50%

        // 6. Query workspace competitor analytics summary
        var analytics = await owner.Client.GetFromJsonAsync<CompetitorAnalyticsSummaryDto>("/api/v1/competitors/analytics");
        analytics.ShouldNotBeNull();
        analytics.TotalCompetitors.ShouldBeGreaterThanOrEqualTo(1);
        analytics.TotalDealsWithCompetitors.ShouldBe(3);
        analytics.OverallWinRateAgainstCompetitors.ShouldBe(50.00m);
    }

    [Fact]
    public async Task Multi_tenant_isolation_for_competitors()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        await EnableFeatureAsync(tenantA);
        await EnableFeatureAsync(tenantB);

        // Tenant A creates competitor "Contoso"
        var createA = await tenantA.Client.PostAsJsonAsync("/api/v1/competitors", new SaveCompetitorRequest("Contoso Private"));
        createA.StatusCode.ShouldBe(HttpStatusCode.OK);
        var compA = await createA.Content.ReadFromJsonAsync<CompetitorDto>();

        // Tenant B lists competitors -> Contoso is not present
        var listB = await tenantB.Client.GetFromJsonAsync<List<CompetitorDto>>("/api/v1/competitors");
        listB.ShouldNotBeNull();
        listB.ShouldNotContain(c => c.Name == "Contoso Private");

        // Tenant B attempts to get Tenant A's competitor -> 404
        var getB = await tenantB.Client.GetAsync($"/api/v1/competitors/{compA!.Id}");
        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B creates deal and attempts to attach Tenant A's competitor -> 404
        var dealB = await SetupDealAsync(tenantB, "Tenant B Deal");
        var attachB = await tenantB.Client.PostAsJsonAsync(
            $"/api/v1/deals/{dealB.Id}/competitors",
            new AddDealCompetitorRequest(compA.Id));
        attachB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
