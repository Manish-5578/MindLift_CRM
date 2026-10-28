using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Automation;

public sealed class RoutingTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task<Guid> GetUserIdAsync(Session session)
    {
        var me = await session.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        return me!.UserId;
    }

    private static SaveLeadRequest NewLead(string first, string last, string email, Guid? ownerId = null)
        => new(first, last, email, null, "Acme Corp", "new", "website", null, null, null, ownerId, null);

    [Fact]
    public async Task Returns_404_when_lead_routing_feature_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var req = new SaveTerritoryRequest
        {
            Name = "Disabled Territory",
            RoutingStrategy = "RoundRobin"
        };

        var postResp = await admin.Client.PostAsJsonAsync("/api/v1/territories", req);
        postResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var listResp = await admin.Client.GetAsync("/api/v1/territories");
        listResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var simResp = await admin.Client.PostAsJsonAsync("/api/v1/routing/simulate", new SimulateRoutingRequest { Country = "US" });
        simResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task End_to_end_territory_crud_and_membership()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/lead-routing", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var rep1 = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "rep-one");
        var rep2 = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "rep-two");
        var rep1Id = await GetUserIdAsync(rep1);
        var rep2Id = await GetUserIdAsync(rep2);

        // 1. Create territory
        var territoryReq = new SaveTerritoryRequest
        {
            Name = "West Coast Tech",
            Description = "Territory covering California tech accounts",
            Priority = 1,
            RoutingStrategy = "RoundRobin",
            CountryCodes = ["US"],
            Regions = ["California"],
            Industries = ["Technology"],
            MinRevenue = 500000m,
            Members =
            [
                new SaveTerritoryMemberRequest { UserId = rep1Id, Weight = 1, IsActive = true },
                new SaveTerritoryMemberRequest { UserId = rep2Id, Weight = 2, IsActive = true }
            ]
        };

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/territories", territoryReq);
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var territory = await createResp.Content.ReadFromJsonAsync<TerritoryDto>();
        territory.ShouldNotBeNull();
        territory.Name.ShouldBe("West Coast Tech");
        territory.Members.Count.ShouldBe(2);

        // 2. Get by Id
        var getResp = await admin.Client.GetAsync($"/api/v1/territories/{territory.Id}");
        getResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var retrieved = await getResp.Content.ReadFromJsonAsync<TerritoryDto>();
        retrieved.ShouldNotBeNull();
        retrieved.Id.ShouldBe(territory.Id);

        // 3. Update territory
        territoryReq.Name = "West Coast Enterprise Tech";
        territoryReq.Priority = 2;
        var updateResp = await admin.Client.PutAsJsonAsync($"/api/v1/territories/{territory.Id}", territoryReq);
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<TerritoryDto>();
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("West Coast Enterprise Tech");
        updated.Priority.ShouldBe(2);

        // 4. Remove a member
        var removeResp = await admin.Client.DeleteAsync($"/api/v1/territories/{territory.Id}/members/{rep2Id}");
        removeResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var membersResp = await admin.Client.GetAsync($"/api/v1/territories/{territory.Id}/members");
        membersResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var members = await membersResp.Content.ReadFromJsonAsync<List<TerritoryMemberDto>>();
        members.ShouldNotBeNull();
        members.Count.ShouldBe(1);
        members[0].UserId.ShouldBe(rep1Id);

        // 5. Delete territory
        var deleteResp = await admin.Client.DeleteAsync($"/api/v1/territories/{territory.Id}");
        deleteResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getAfterDelete = await admin.Client.GetAsync($"/api/v1/territories/{territory.Id}");
        getAfterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RoundRobin_routing_cycles_through_territory_members()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/lead-routing", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var repA = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "rep-a");
        var repB = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "rep-b");
        var repAId = await GetUserIdAsync(repA);
        var repBId = await GetUserIdAsync(repB);

        // Create Round-Robin Territory for US
        var territoryReq = new SaveTerritoryRequest
        {
            Name = "US Inbound",
            Priority = 1,
            RoutingStrategy = "RoundRobin",
            CountryCodes = ["US"],
            Members =
            [
                new SaveTerritoryMemberRequest { UserId = repAId, Weight = 1, IsActive = true },
                new SaveTerritoryMemberRequest { UserId = repBId, Weight = 1, IsActive = true }
            ]
        };
        var tResp = await admin.Client.PostAsJsonAsync("/api/v1/territories", territoryReq);
        tResp.EnsureSuccessStatusCode();

        // Create 3 Leads
        var l1Resp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Lead", "One", "lead1@example.com"));
        var lead1 = (await l1Resp.Content.ReadFromJsonAsync<LeadDto>())!;

        var l2Resp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Lead", "Two", "lead2@example.com"));
        var lead2 = (await l2Resp.Content.ReadFromJsonAsync<LeadDto>())!;

        var l3Resp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Lead", "Three", "lead3@example.com"));
        var lead3 = (await l3Resp.Content.ReadFromJsonAsync<LeadDto>())!;

        // Route Lead 1 (Country = US) -> Should go to first member (repA)
        var route1 = await admin.Client.PostAsJsonAsync($"/api/v1/routing/route-lead/{lead1.Id}", new RouteLeadRequest { Country = "US" });
        route1.StatusCode.ShouldBe(HttpStatusCode.OK);
        var res1 = await route1.Content.ReadFromJsonAsync<RouteLeadResultDto>();
        res1.ShouldNotBeNull();
        res1.Success.ShouldBeTrue();
        var firstAssigned = res1.AssignedUserId!.Value;
        var secondExpected = firstAssigned == repAId ? repBId : repAId;

        // Route Lead 2 (Country = US) -> Should cycle to second member
        var route2 = await admin.Client.PostAsJsonAsync($"/api/v1/routing/route-lead/{lead2.Id}", new RouteLeadRequest { Country = "US" });
        route2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var res2 = await route2.Content.ReadFromJsonAsync<RouteLeadResultDto>();
        res2.ShouldNotBeNull();
        res2.Success.ShouldBeTrue();
        res2.AssignedUserId.ShouldBe(secondExpected);

        // Route Lead 3 (Country = US) -> Should cycle back to first member
        var route3 = await admin.Client.PostAsJsonAsync($"/api/v1/routing/route-lead/{lead3.Id}", new RouteLeadRequest { Country = "US" });
        route3.StatusCode.ShouldBe(HttpStatusCode.OK);
        var res3 = await route3.Content.ReadFromJsonAsync<RouteLeadResultDto>();
        res3.ShouldNotBeNull();
        res3.Success.ShouldBeTrue();
        res3.AssignedUserId.ShouldBe(firstAssigned);

        // Verify routing logs were recorded
        var logsResp = await admin.Client.GetAsync("/api/v1/routing/logs");
        logsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var logs = await logsResp.Content.ReadFromJsonAsync<List<LeadRoutingLogDto>>();
        logs.ShouldNotBeNull();
        logs.Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Territory_matching_with_postal_code_prefix_and_industry()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/lead-routing", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var bayAreaRep = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "bay-rep");
        var healthRep = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "health-rep");
        var bayRepId = await GetUserIdAsync(bayAreaRep);
        var healthRepId = await GetUserIdAsync(healthRep);

        // Territory 1: Bay Area Zip prefix 94*
        var t1Req = new SaveTerritoryRequest
        {
            Name = "SF Bay Area",
            Priority = 1,
            PostalCodes = ["94*"],
            Members = [new SaveTerritoryMemberRequest { UserId = bayRepId }]
        };
        (await admin.Client.PostAsJsonAsync("/api/v1/territories", t1Req)).EnsureSuccessStatusCode();

        // Territory 2: Healthcare Industry
        var t2Req = new SaveTerritoryRequest
        {
            Name = "Healthcare Sector",
            Priority = 2,
            Industries = ["Healthcare", "Medical"],
            Members = [new SaveTerritoryMemberRequest { UserId = healthRepId }]
        };
        (await admin.Client.PostAsJsonAsync("/api/v1/territories", t2Req)).EnsureSuccessStatusCode();

        // Simulate zip code 94107 -> Should match SF Bay Area
        var sim1 = await admin.Client.PostAsJsonAsync("/api/v1/routing/simulate", new SimulateRoutingRequest
        {
            PostalCode = "94107",
            Industry = "Finance"
        });
        sim1.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sim1Res = await sim1.Content.ReadFromJsonAsync<SimulateRoutingResultDto>();
        sim1Res.ShouldNotBeNull();
        sim1Res.Matched.ShouldBeTrue();
        sim1Res.MatchedTerritoryName.ShouldBe("SF Bay Area");
        sim1Res.CandidateUserId.ShouldBe(bayRepId);

        // Simulate non-94 zip code with Healthcare industry -> Should match Healthcare Sector
        var sim2 = await admin.Client.PostAsJsonAsync("/api/v1/routing/simulate", new SimulateRoutingRequest
        {
            PostalCode = "10001",
            Industry = "Healthcare"
        });
        sim2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sim2Res = await sim2.Content.ReadFromJsonAsync<SimulateRoutingResultDto>();
        sim2Res.ShouldNotBeNull();
        sim2Res.Matched.ShouldBeTrue();
        sim2Res.MatchedTerritoryName.ShouldBe("Healthcare Sector");
        sim2Res.CandidateUserId.ShouldBe(healthRepId);
    }

    [Fact]
    public async Task LoadBalanced_routing_favors_rep_with_fewest_active_leads()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/lead-routing", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var busyRep = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "busy-rep");
        var freeRep = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "free-rep");
        var busyRepId = await GetUserIdAsync(busyRep);
        var freeRepId = await GetUserIdAsync(freeRep);

        // Pre-create leads assigned to busyRep
        (await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Busy", "One", "busy1@test.com", busyRepId))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Busy", "Two", "busy2@test.com", busyRepId))).EnsureSuccessStatusCode();

        // Create territory with LoadBalanced strategy
        var tReq = new SaveTerritoryRequest
        {
            Name = "Balanced Support Pipeline",
            RoutingStrategy = "LoadBalanced",
            CountryCodes = ["US"],
            Members =
            [
                new SaveTerritoryMemberRequest { UserId = busyRepId, Weight = 1 },
                new SaveTerritoryMemberRequest { UserId = freeRepId, Weight = 1 }
            ]
        };
        (await admin.Client.PostAsJsonAsync("/api/v1/territories", tReq)).EnsureSuccessStatusCode();

        // Create new lead and route it
        var newLeadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Fresh", "Inbound", "fresh@test.com"));
        var newLead = (await newLeadResp.Content.ReadFromJsonAsync<LeadDto>())!;

        var routeResp = await admin.Client.PostAsJsonAsync($"/api/v1/routing/route-lead/{newLead.Id}", new RouteLeadRequest { Country = "US" });
        routeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var routeResult = await routeResp.Content.ReadFromJsonAsync<RouteLeadResultDto>();
        routeResult.ShouldNotBeNull();
        routeResult.Success.ShouldBeTrue();
        // Because freeRep has 0 leads and busyRep has 2, freeRep must be chosen!
        routeResult.AssignedUserId.ShouldBe(freeRepId);
    }

    [Fact]
    public async Task Weighted_routing_distributes_proportional_to_member_weights()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/lead-routing", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var heavyRep = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "heavy-rep");
        var lightRep = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "light-rep");
        var heavyRepId = await GetUserIdAsync(heavyRep);
        var lightRepId = await GetUserIdAsync(lightRep);

        // Heavy rep gets weight 3, light rep gets weight 1
        var tReq = new SaveTerritoryRequest
        {
            Name = "Weighted Channel",
            RoutingStrategy = "Weighted",
            CountryCodes = ["CA"],
            Members =
            [
                new SaveTerritoryMemberRequest { UserId = heavyRepId, Weight = 3 },
                new SaveTerritoryMemberRequest { UserId = lightRepId, Weight = 1 }
            ]
        };
        (await admin.Client.PostAsJsonAsync("/api/v1/territories", tReq)).EnsureSuccessStatusCode();

        var assignedUserIds = new List<Guid>();

        for (int i = 0; i < 4; i++)
        {
            var lResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead($"Weight", $"Lead{i}", $"wl{i}@test.ca"));
            var l = (await lResp.Content.ReadFromJsonAsync<LeadDto>())!;

            var rResp = await admin.Client.PostAsJsonAsync($"/api/v1/routing/route-lead/{l.Id}", new RouteLeadRequest { Country = "CA" });
            rResp.StatusCode.ShouldBe(HttpStatusCode.OK);
            var r = (await rResp.Content.ReadFromJsonAsync<RouteLeadResultDto>())!;
            assignedUserIds.Add(r.AssignedUserId!.Value);
        }

        // Out of 4 leads, heavy rep (weight 3) should have received 3 and light rep (weight 1) should have received 1
        assignedUserIds.Count(id => id == heavyRepId).ShouldBe(3);
        assignedUserIds.Count(id => id == lightRepId).ShouldBe(1);
    }

    [Fact]
    public async Task Multi_tenant_territory_isolation()
    {
        var tenantA = await Workspaces.SignupAsync(factory, "Tenant Alpha");
        var tenantB = await Workspaces.SignupAsync(factory, "Tenant Beta");

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/lead-routing", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/lead-routing", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates a territory
        var createResp = await tenantA.Client.PostAsJsonAsync("/api/v1/territories", new SaveTerritoryRequest
        {
            Name = "Alpha Territory",
            CountryCodes = ["GB"]
        });
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var alphaTerritory = (await createResp.Content.ReadFromJsonAsync<TerritoryDto>())!;

        // Tenant B lists territories -> should be empty
        var bListResp = await tenantB.Client.GetAsync("/api/v1/territories");
        bListResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bList = (await bListResp.Content.ReadFromJsonAsync<List<TerritoryDto>>())!;
        bList.ShouldBeEmpty();

        // Tenant B cannot get Tenant A's territory -> 404
        var bGetResp = await tenantB.Client.GetAsync($"/api/v1/territories/{alphaTerritory.Id}");
        bGetResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
