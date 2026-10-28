using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Reporting;

public sealed class VelocityReportsTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveDealRequest NewDeal(string name, decimal amount, Guid? stageId = null, string? status = "Open")
        => new(name, null, stageId, amount, null, 50, DateTime.UtcNow.AddMonths(1), null, null, "Velocity test deal", null, null);

    [Fact]
    public async Task Feature_flag_gates_velocity_reports_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Feature is OFF by default -> 404
        var getOff = await admin.Client.GetAsync("/api/v1/reports/velocity/funnel");
        getOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 2. Enable feature
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/velocity-reports", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // 3. Feature endpoint now succeeds -> 200 OK
        var getOn = await admin.Client.GetAsync("/api/v1/reports/velocity/funnel");
        getOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pipeline_funnel_report_metrics_and_stage_dwell_times()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/velocity-reports", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var pipelines = await admin.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines");
        pipelines.ShouldNotBeNull();
        var defaultPipeline = pipelines.First();
        var stages = defaultPipeline.Stages;

        var firstStage = stages.OrderBy(s => s.SortOrder).First();
        var wonStage = stages.FirstOrDefault(s => s.StageType == "Won") ?? stages.Last();
        var lostStage = stages.FirstOrDefault(s => s.StageType == "Lost") ?? stages.Last();

        // Create 3 deals: Won ($100k), Open ($50k), Lost ($30k)
        var d1Resp = await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Deal Enterprise", 100000m, firstStage.Id));
        d1Resp.EnsureSuccessStatusCode();
        var d1 = await d1Resp.Content.ReadFromJsonAsync<DealDto>();
        (await admin.Client.PatchAsJsonAsync($"/api/v1/deals/{d1!.Id}/stage", new ChangeDealStageRequest(wonStage.Id, null, null, null, null))).EnsureSuccessStatusCode();

        var d2Resp = await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Deal MidMarket", 50000m, firstStage.Id));
        d2Resp.EnsureSuccessStatusCode();

        var d3Resp = await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Deal Small", 30000m, firstStage.Id));
        d3Resp.EnsureSuccessStatusCode();
        var d3 = await d3Resp.Content.ReadFromJsonAsync<DealDto>();
        (await admin.Client.PatchAsJsonAsync($"/api/v1/deals/{d3!.Id}/stage", new ChangeDealStageRequest(lostStage.Id, "price", "Price too high", null, null))).EnsureSuccessStatusCode();

        // Query Funnel Report
        var funnelResp = await admin.Client.GetAsync($"/api/v1/reports/velocity/funnel?pipelineId={defaultPipeline.Id}");
        funnelResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var funnel = await funnelResp.Content.ReadFromJsonAsync<PipelineFunnelReportDto>();

        funnel.ShouldNotBeNull();
        funnel.TotalDealsEntered.ShouldBe(3);
        funnel.TotalDealsWon.ShouldBe(1);
        funnel.TotalDealsLost.ShouldBe(1);
        funnel.TotalPipelineValue.ShouldBe(180000m);
        funnel.WonValue.ShouldBe(100000m);
        funnel.Stages.ShouldNotBeEmpty();

        var firstStageMetric = funnel.Stages.First(s => s.StageId == firstStage.Id);
        firstStageMetric.DealsCount.ShouldBeGreaterThanOrEqualTo(1);
        firstStageMetric.AverageDurationDays.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Sales_velocity_equation_calculation()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/velocity-reports", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var pipelines = await admin.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines");
        var defaultPipeline = pipelines!.First();
        var stages = defaultPipeline.Stages;
        var firstStage = stages.OrderBy(s => s.SortOrder).First();
        var wonStage = stages.FirstOrDefault(s => s.StageType == "Won") ?? stages.Last();

        // Create closed-won deal
        var dResp = await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Velocity Winner", 120000m, firstStage.Id));
        dResp.EnsureSuccessStatusCode();
        var deal = await dResp.Content.ReadFromJsonAsync<DealDto>();
        (await admin.Client.PatchAsJsonAsync($"/api/v1/deals/{deal!.Id}/stage", new ChangeDealStageRequest(wonStage.Id, null, null, null, null))).EnsureSuccessStatusCode();

        // Query Velocity Report
        var velResp = await admin.Client.GetAsync($"/api/v1/reports/velocity/sales-velocity?pipelineId={defaultPipeline.Id}");
        velResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var velocity = await velResp.Content.ReadFromJsonAsync<SalesVelocityReportDto>();

        velocity.ShouldNotBeNull();
        velocity.NumberOfOpportunities.ShouldBe(1);
        velocity.WinRatePercentage.ShouldBe(100.0m);
        velocity.AverageDealSize.ShouldBe(120000m);
        velocity.SalesCycleLengthDays.ShouldBeGreaterThanOrEqualTo(1.0);
        velocity.SalesVelocityPerDay.ShouldBeGreaterThan(0m);
        velocity.RepVelocities.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_velocity_data_leakage()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await tenantB.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/velocity-reports", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/velocity-reports", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates a high value deal
        (await tenantA.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Tenant A Secret Deal", 500000m))).EnsureSuccessStatusCode();

        // Tenant B queries sales velocity -> must be 0
        var velBResp = await tenantB.Client.GetAsync("/api/v1/reports/velocity/sales-velocity");
        velBResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var velB = await velBResp.Content.ReadFromJsonAsync<SalesVelocityReportDto>();

        velB.ShouldNotBeNull();
        velB.NumberOfOpportunities.ShouldBe(0);
        velB.SalesVelocityPerDay.ShouldBe(0m);
        velB.RepVelocities.ShouldBeEmpty();
    }
}
