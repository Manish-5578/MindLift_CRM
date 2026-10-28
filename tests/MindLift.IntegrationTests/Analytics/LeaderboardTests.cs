using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Analytics;

public sealed class LeaderboardTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveDealRequest NewDeal(string name, decimal amount, Guid stageId)
        => new(name, null, stageId, amount, null, 50, DateTime.UtcNow.AddMonths(1), null, null, "Leaderboard test deal", null, null);

    [Fact]
    public async Task Feature_flag_gates_leaderboards_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Feature is OFF by default -> 404
        var getOff = await admin.Client.GetAsync("/api/v1/leaderboards");
        getOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 2. Enable feature
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/leaderboards", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // 3. Feature endpoint now succeeds -> 200 OK
        var getOn = await admin.Client.GetAsync("/api/v1/leaderboards");
        getOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Leaderboard_revenue_rankings_podium_and_relative_gap()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/leaderboards", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Invite and onboard staff rep in the same workspace
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff");

        var pipelines = await admin.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines");
        var defaultPipeline = pipelines!.First();
        var stages = defaultPipeline.Stages;
        var firstStage = stages.OrderBy(s => s.SortOrder).First();
        var wonStage = stages.FirstOrDefault(s => s.StageType == "Won") ?? stages.Last();

        // Admin wins $100k deal
        var adminDealResp = await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Admin Big Deal", 100000m, firstStage.Id));
        adminDealResp.EnsureSuccessStatusCode();
        var adminDeal = await adminDealResp.Content.ReadFromJsonAsync<DealDto>();
        (await admin.Client.PatchAsJsonAsync($"/api/v1/deals/{adminDeal!.Id}/stage", new ChangeDealStageRequest(wonStage.Id, null, null, null, null))).EnsureSuccessStatusCode();

        // Staff wins $40k deal
        var staffDealResp = await staff.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Staff Deal", 40000m, firstStage.Id));
        staffDealResp.EnsureSuccessStatusCode();
        var staffDeal = await staffDealResp.Content.ReadFromJsonAsync<DealDto>();
        (await staff.Client.PatchAsJsonAsync($"/api/v1/deals/{staffDeal!.Id}/stage", new ChangeDealStageRequest(wonStage.Id, null, null, null, null))).EnsureSuccessStatusCode();

        // 1. Admin queries Leaderboard
        var adminLeaderboard = await admin.Client.GetFromJsonAsync<LeaderboardResponseDto>("/api/v1/leaderboards?metric=revenue");
        adminLeaderboard.ShouldNotBeNull();
        adminLeaderboard.Entries.Count.ShouldBeGreaterThanOrEqualTo(2);

        var top = adminLeaderboard.Entries[0];
        top.UserEmail.ShouldBe(admin.Email);
        top.Rank.ShouldBe(1);
        top.Score.ShouldBe(100000m);
        top.Tier.ShouldBe("Diamond");

        var runnerUp = adminLeaderboard.Entries[1];
        runnerUp.UserEmail.ShouldBe(staff.Email);
        runnerUp.Rank.ShouldBe(2);
        runnerUp.Score.ShouldBe(40000m);
        runnerUp.Tier.ShouldBe("Gold");

        // 2. Staff queries Leaderboard and checks relative gap
        var staffLeaderboard = await staff.Client.GetFromJsonAsync<LeaderboardResponseDto>("/api/v1/leaderboards?metric=revenue");
        staffLeaderboard.ShouldNotBeNull();
        staffLeaderboard.MyRank.ShouldNotBeNull();
        staffLeaderboard.MyRank.Rank.ShouldBe(2);
        staffLeaderboard.MyRank.GapToNextRank.ShouldBe(60000m);

        // 3. Deals won count metric
        var dealsWonBoard = await admin.Client.GetFromJsonAsync<LeaderboardResponseDto>("/api/v1/leaderboards?metric=deals_won");
        dealsWonBoard.ShouldNotBeNull();
        dealsWonBoard.Entries.First(e => e.UserEmail == admin.Email).Score.ShouldBe(1m);
    }

    [Fact]
    public async Task Metadata_endpoint_returns_supported_metrics()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/leaderboards", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var meta = await admin.Client.GetFromJsonAsync<LeaderboardMetaDto>("/api/v1/leaderboards/meta");
        meta.ShouldNotBeNull();
        meta.Metrics.ShouldContain("revenue");
        meta.Metrics.ShouldContain("deals_won");
        meta.Periods.ShouldContain("this_month");
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_leaderboard_leakage()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await tenantB.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/leaderboards", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/leaderboards", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates and wins a huge deal
        var pipelinesA = await tenantA.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines");
        var wonStageA = pipelinesA!.First().Stages.First(s => s.StageType == "Won");
        var firstStageA = pipelinesA!.First().Stages.First(s => s.SortOrder == 1);

        var dealAResp = await tenantA.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Secret Deal", 1000000m, firstStageA.Id));
        var dealA = await dealAResp.Content.ReadFromJsonAsync<DealDto>();
        (await tenantA.Client.PatchAsJsonAsync($"/api/v1/deals/{dealA!.Id}/stage", new ChangeDealStageRequest(wonStageA.Id, null, null, null, null))).EnsureSuccessStatusCode();

        // Tenant B queries leaderboard: must not contain Tenant A user or score
        var boardB = await tenantB.Client.GetFromJsonAsync<LeaderboardResponseDto>("/api/v1/leaderboards?metric=revenue");
        boardB.ShouldNotBeNull();
        boardB.Entries.Any(e => e.UserEmail == tenantA.Email).ShouldBeFalse();
        boardB.Entries.All(e => e.Score == 0m).ShouldBeTrue();
    }
}
