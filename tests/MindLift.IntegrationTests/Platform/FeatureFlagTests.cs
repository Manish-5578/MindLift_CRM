using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Platform;

/// <summary>Phase 0 / F1: every new capability is OFF until the company owner switches it on, and one company cannot affect another.</summary>
public sealed class FeatureFlagTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Every_feature_starts_off_and_the_catalog_is_complete()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var list = (await owner.Client.GetFromJsonAsync<List<FeatureFlagDto>>("/api/v1/features"))!;

        list.Count.ShouldBe(FeatureCatalog.All.Count);
        list.ShouldAllBe(f => !f.Enabled);
        list.Select(f => f.Key).Distinct().Count().ShouldBe(list.Count);   // no duplicate keys
    }

    [Fact]
    public async Task The_owner_can_switch_a_feature_on_and_off_and_it_is_remembered()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var on = await owner.Client.PutAsJsonAsync("/api/v1/features/deal-stage-gating", new SetFeatureRequest(true));
        on.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await on.Content.ReadFromJsonAsync<FeatureFlagDto>())!.Enabled.ShouldBeTrue();

        var list = (await owner.Client.GetFromJsonAsync<List<FeatureFlagDto>>("/api/v1/features"))!;
        list.Single(f => f.Key == "deal-stage-gating").Enabled.ShouldBeTrue();
        list.Count(f => f.Enabled).ShouldBe(1);   // nothing else was touched

        (await owner.Client.PutAsJsonAsync("/api/v1/features/deal-stage-gating", new SetFeatureRequest(false))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.Client.GetFromJsonAsync<List<FeatureFlagDto>>("/api/v1/features"))!.ShouldAllBe(f => !f.Enabled);
    }

    [Fact]
    public async Task An_unknown_feature_key_is_refused()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var response = await owner.Client.PutAsJsonAsync("/api/v1/features/not-a-real-feature", new SetFeatureRequest(true));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task One_company_switching_a_feature_on_does_not_turn_it_on_for_another()
    {
        var a = await Workspaces.SignupAsync(factory);
        var b = await Workspaces.SignupAsync(factory);

        (await a.Client.PutAsJsonAsync("/api/v1/features/forecasting", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        (await a.Client.GetFromJsonAsync<List<FeatureFlagDto>>("/api/v1/features"))!.Single(f => f.Key == "forecasting").Enabled.ShouldBeTrue();
        (await b.Client.GetFromJsonAsync<List<FeatureFlagDto>>("/api/v1/features"))!.Single(f => f.Key == "forecasting").Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Without_permission_or_without_signing_in_nothing_can_be_switched()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, owner, "Staff", "staff-features");

        (await staff.Client.PutAsJsonAsync("/api/v1/features/forecasting", new SetFeatureRequest(true))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClient().PutAsJsonAsync("/api/v1/features/forecasting", new SetFeatureRequest(true))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await factory.CreateClient().GetAsync("/api/v1/features")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await owner.Client.GetFromJsonAsync<List<FeatureFlagDto>>("/api/v1/features"))!.ShouldAllBe(f => !f.Enabled);
    }
}
