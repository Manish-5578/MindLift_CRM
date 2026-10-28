using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Deals;

/// <summary>
/// Phase 3 / #15: Forecasting and Weighted Pipeline (forecasting).
/// Integration tests verifying feature flag gating, forecast category resolution,
/// probability-weighted pipeline rollups, category overrides, quota attainment, and multi-tenant isolation.
/// </summary>
public sealed class ForecastingTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s)
        => (await s.Client.PutAsJsonAsync("/api/v1/features/forecasting", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    private static async Task<DealDto> SetupDealAsync(
        Session s,
        string dealName,
        decimal amount,
        int probability,
        DateTime? closeDate = null)
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
            probability,
            closeDate ?? DateTime.UtcNow.AddDays(15),
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
    public async Task Feature_flag_off_blocks_forecasting_endpoints()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var deal = await SetupDealAsync(owner, "Sample Deal", 50000m, 50);

        // GET summary blocked (404)
        var summaryRes = await owner.Client.GetAsync("/api/v1/forecasting/summary");
        summaryRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // GET users blocked (404)
        var usersRes = await owner.Client.GetAsync("/api/v1/forecasting/users");
        usersRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // GET categories blocked (404)
        var catRes = await owner.Client.GetAsync("/api/v1/forecasting/categories");
        catRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // PUT category blocked (404)
        var putRes = await owner.Client.PutAsJsonAsync($"/api/v1/forecasting/deals/{deal.Id}/category", new UpdateDealForecastCategoryRequest("Commit"));
        putRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Weighted_pipeline_categories_and_quota_attainment_calculation()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);

        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        me.ShouldNotBeNull();

        // 1. Setup a sales target for owner (Quota = $200,000 for current month/quarter)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var targetReq = new SaveSalesTargetRequest(
            me.UserId,
            null,
            SalesTargetMetric.WonAmount,
            SalesTargetPeriod.Month,
            today.AddDays(-10),
            today.AddDays(50),
            200000m
        );
        var targetRes = await owner.Client.PostAsJsonAsync("/api/v1/sales-targets", targetReq);
        targetRes.EnsureSuccessStatusCode();

        // 2. Create 3 deals with different probabilities
        // Deal 1: $100,000 at 90% -> Auto-resolves to Commit ($90,000 weighted)
        var deal1 = await SetupDealAsync(owner, "Enterprise ERP", 100000m, 90);
        // Deal 2: $50,000 at 60% -> Auto-resolves to BestCase ($30,000 weighted)
        var deal2 = await SetupDealAsync(owner, "Consulting Retainer", 50000m, 60);
        // Deal 3: $20,000 at 20% -> Auto-resolves to Pipeline ($4,000 weighted)
        var deal3 = await SetupDealAsync(owner, "Pilot Project", 20000m, 20);

        // 3. Verify category breakdown endpoint
        var categories = await owner.Client.GetFromJsonAsync<List<ForecastCategorySummaryDto>>("/api/v1/forecasting/categories");
        categories.ShouldNotBeNull();
        categories.Count.ShouldBe(5);

        var commitCat = categories.Single(c => c.Category == ForecastCategories.Commit);
        commitCat.DealCount.ShouldBe(1);
        commitCat.TotalAmount.ShouldBe(100000m);
        commitCat.WeightedAmount.ShouldBe(90000m);

        var bestCaseCat = categories.Single(c => c.Category == ForecastCategories.BestCase);
        bestCaseCat.DealCount.ShouldBe(1);
        bestCaseCat.TotalAmount.ShouldBe(50000m);
        bestCaseCat.WeightedAmount.ShouldBe(30000m);

        var pipelineCat = categories.Single(c => c.Category == ForecastCategories.Pipeline);
        pipelineCat.DealCount.ShouldBe(1);
        pipelineCat.TotalAmount.ShouldBe(20000m);
        pipelineCat.WeightedAmount.ShouldBe(4000m);

        // 4. Verify overall forecast summary
        var summary = await owner.Client.GetFromJsonAsync<ForecastSummaryDto>("/api/v1/forecasting/summary");
        summary.ShouldNotBeNull();
        summary.TotalQuota.ShouldBe(200000m);
        summary.TotalPipelineAmount.ShouldBe(170000m); // 100k + 50k + 20k
        summary.WeightedPipelineAmount.ShouldBe(124000m); // 90k + 30k + 4k
        summary.CommitAmount.ShouldBe(100000m);
        summary.BestCaseAmount.ShouldBe(50000m);
        summary.ClosedWonAmount.ShouldBe(0m);
        summary.ProjectedAttainmentAmount.ShouldBe(124000m);
        summary.AttainmentPercentage.ShouldBe(62.00m); // 124,000 / 200,000 = 62%
        summary.GapToTarget.ShouldBe(76000m);

        // 5. Update Deal 3 to explicit "Commit" category override
        var updateCatRes = await owner.Client.PutAsJsonAsync(
            $"/api/v1/forecasting/deals/{deal3.Id}/category",
            new UpdateDealForecastCategoryRequest(ForecastCategories.Commit));
        updateCatRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedDeal3 = await updateCatRes.Content.ReadFromJsonAsync<DealDto>();
        updatedDeal3.ShouldNotBeNull();
        updatedDeal3.ForecastCategory.ShouldBe(ForecastCategories.Commit);

        // 6. Verify summary reflects override (Deal 3 now in Commit category)
        var summaryAfterOverride = await owner.Client.GetFromJsonAsync<ForecastSummaryDto>("/api/v1/forecasting/summary");
        summaryAfterOverride.ShouldNotBeNull();
        summaryAfterOverride.CommitAmount.ShouldBe(120000m); // 100k + 20k

        // 7. Verify user attainments endpoint
        var userAttainments = await owner.Client.GetFromJsonAsync<List<UserForecastAttainmentDto>>("/api/v1/forecasting/users");
        userAttainments.ShouldNotBeNull();
        userAttainments.Count.ShouldBeGreaterThanOrEqualTo(1);
        var ownerAttainment = userAttainments.Single(u => u.UserId == me.UserId);
        ownerAttainment.QuotaTarget.ShouldBe(200000m);
        ownerAttainment.CommitAmount.ShouldBe(120000m);
        ownerAttainment.ProjectedTotal.ShouldBe(124000m);
    }

    [Fact]
    public async Task Multi_tenant_isolation_for_forecasting()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        await EnableFeatureAsync(tenantA);
        await EnableFeatureAsync(tenantB);

        var dealA = await SetupDealAsync(tenantA, "Tenant A Classified Deal", 80000m, 90);

        // Tenant A summary has dealA
        var summaryA = await tenantA.Client.GetFromJsonAsync<ForecastSummaryDto>("/api/v1/forecasting/summary");
        summaryA.ShouldNotBeNull();
        summaryA.TotalPipelineAmount.ShouldBe(80000m);
        summaryA.WeightedPipelineAmount.ShouldBe(72000m);

        // Tenant B summary has 0 deals
        var summaryB = await tenantB.Client.GetFromJsonAsync<ForecastSummaryDto>("/api/v1/forecasting/summary");
        summaryB.ShouldNotBeNull();
        summaryB.TotalPipelineAmount.ShouldBe(0m);
        summaryB.WeightedPipelineAmount.ShouldBe(0m);

        // Tenant B cannot modify Tenant A deal category -> 404
        var putResB = await tenantB.Client.PutAsJsonAsync(
            $"/api/v1/forecasting/deals/{dealA.Id}/category",
            new UpdateDealForecastCategoryRequest(ForecastCategories.Commit));
        putResB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
