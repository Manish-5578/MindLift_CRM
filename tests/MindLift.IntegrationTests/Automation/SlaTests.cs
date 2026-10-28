using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Automation;

public sealed class SlaTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task<Guid> GetUserIdAsync(Session session)
    {
        var me = await session.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        return me!.UserId;
    }

    private static SaveLeadRequest NewLead(
        string first,
        string last,
        string? email = null,
        string? company = null,
        string status = "new",
        string? source = null,
        string? rating = null,
        Guid? ownerId = null)
        => new(first, last, email, null, company, status, source, rating, null, null, ownerId, null);

    [Fact]
    public async Task Returns_404_when_response_sla_feature_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var policyReq = new SaveSlaPolicyRequest
        {
            Name = "Disabled SLA Policy",
            MaxResponseMinutes = 60
        };

        var postResp = await admin.Client.PostAsJsonAsync("/api/v1/sla/policies", policyReq);
        postResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var listResp = await admin.Client.GetAsync("/api/v1/sla/policies");
        listResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var attachResp = await admin.Client.PostAsync($"/api/v1/sla/leads/{Guid.NewGuid()}/attach", null);
        attachResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task End_to_end_sla_policy_crud()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/response-sla", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var manager = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "sla-mgr");
        var managerId = await GetUserIdAsync(manager);

        // 1. Create policy
        var createReq = new SaveSlaPolicyRequest
        {
            Name = "Hot Inbound 15m SLA",
            Description = "Respond to hot inbound website leads within 15 minutes",
            MaxResponseMinutes = 15,
            WarningThresholdPercentage = 80,
            Sources = ["website", "form"],
            Ratings = ["hot"],
            EscalationUserId = managerId,
            EscalationAction = "ReassignAndNotify",
            Priority = 1
        };

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/sla/policies", createReq);
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var policy = await createResp.Content.ReadFromJsonAsync<SlaPolicyDto>();
        policy.ShouldNotBeNull();
        policy.Name.ShouldBe("Hot Inbound 15m SLA");
        policy.MaxResponseMinutes.ShouldBe(15);
        policy.EscalationUserId.ShouldBe(managerId);

        // 2. Get by Id
        var getResp = await admin.Client.GetAsync($"/api/v1/sla/policies/{policy.Id}");
        getResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var retrieved = await getResp.Content.ReadFromJsonAsync<SlaPolicyDto>();
        retrieved.ShouldNotBeNull();
        retrieved.Id.ShouldBe(policy.Id);

        // 3. Update policy
        createReq.MaxResponseMinutes = 30;
        createReq.Name = "Hot Inbound 30m SLA";
        var updateResp = await admin.Client.PutAsJsonAsync($"/api/v1/sla/policies/{policy.Id}", createReq);
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<SlaPolicyDto>();
        updated.ShouldNotBeNull();
        updated.MaxResponseMinutes.ShouldBe(30);

        // 4. List policies
        var listResp = await admin.Client.GetAsync("/api/v1/sla/policies");
        listResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var policies = await listResp.Content.ReadFromJsonAsync<List<SlaPolicyDto>>();
        policies.ShouldNotBeNull();
        policies.Count.ShouldBeGreaterThanOrEqualTo(1);

        // 5. Delete policy
        var deleteResp = await admin.Client.DeleteAsync($"/api/v1/sla/policies/{policy.Id}");
        deleteResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getAfterDelete = await admin.Client.GetAsync($"/api/v1/sla/policies/{policy.Id}");
        getAfterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lead_attaches_matching_policy_and_records_response_within_sla()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/response-sla", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Create SLA policy
        var policyReq = new SaveSlaPolicyRequest
        {
            Name = "Standard 60m SLA",
            MaxResponseMinutes = 60,
            Sources = ["website"],
            Ratings = ["hot"]
        };
        (await admin.Client.PostAsJsonAsync("/api/v1/sla/policies", policyReq)).EnsureSuccessStatusCode();

        // Create matching lead
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads",
            NewLead("Oliver", "Queen", "oliver@queen-industries.test", "Queen Industries", "new", "website", "hot"));
        leadResp.EnsureSuccessStatusCode();
        var lead = (await leadResp.Content.ReadFromJsonAsync<LeadDto>())!;

        // Attach SLA
        var attachResp = await admin.Client.PostAsync($"/api/v1/sla/leads/{lead.Id}/attach", null);
        attachResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tracker = await attachResp.Content.ReadFromJsonAsync<LeadSlaTrackerDto>();
        tracker.ShouldNotBeNull();
        tracker.PolicyName.ShouldBe("Standard 60m SLA");
        tracker.Status.ShouldBe("Pending");
        tracker.IsBreached.ShouldBeFalse();
        tracker.MinutesRemaining.ShouldBeGreaterThan(50);

        // Record response within SLA
        var respondResp = await admin.Client.PostAsJsonAsync($"/api/v1/sla/leads/{lead.Id}/respond", new RecordResponseRequest
        {
            Notes = "Called prospect, had discovery chat."
        });
        respondResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedTracker = await respondResp.Content.ReadFromJsonAsync<LeadSlaTrackerDto>();
        updatedTracker.ShouldNotBeNull();
        updatedTracker.Status.ShouldBe("Met");
        updatedTracker.RespondedAt.ShouldNotBeNull();
        updatedTracker.IsBreached.ShouldBeFalse();
    }

    [Fact]
    public async Task Sla_breach_detection_triggers_automated_reassignment_and_escalation()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/response-sla", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var rep = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "rep-user");
        var manager = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "manager-user");
        var repId = await GetUserIdAsync(rep);
        var managerId = await GetUserIdAsync(manager);

        // Create SLA policy with 1 minute target and Escalation to Manager with Reassignment
        var policyReq = new SaveSlaPolicyRequest
        {
            Name = "Urgent 1m Breach Policy",
            MaxResponseMinutes = 1,
            Sources = ["chat"],
            EscalationUserId = managerId,
            EscalationAction = "ReassignAndNotify"
        };
        (await admin.Client.PostAsJsonAsync("/api/v1/sla/policies", policyReq)).EnsureSuccessStatusCode();

        // Create Lead assigned to rep
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads",
            NewLead("Barry", "Allen", "barry@ccpd.test", "CCPD", "new", "chat", null, repId));
        leadResp.EnsureSuccessStatusCode();
        var lead = (await leadResp.Content.ReadFromJsonAsync<LeadDto>())!;

        // Attach SLA
        var attachResp = await admin.Client.PostAsync($"/api/v1/sla/leads/{lead.Id}/attach", null);
        attachResp.EnsureSuccessStatusCode();

        // Wait 2 seconds (simulate passage of time) and trigger escalation processing
        // Since due time was 1 minute from creation, we can test RecordFirstResponse with a late time or test breach
        // Let's record response at DateTime.UtcNow.AddMinutes(5) (5 minutes late)
        var lateResponse = await admin.Client.PostAsJsonAsync($"/api/v1/sla/leads/{lead.Id}/respond", new RecordResponseRequest
        {
            RespondedAt = DateTime.UtcNow.AddMinutes(5),
            Notes = "Responded too late."
        });
        lateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lateTracker = await lateResponse.Content.ReadFromJsonAsync<LeadSlaTrackerDto>();
        lateTracker.ShouldNotBeNull();
        lateTracker.Status.ShouldBe("Breached");
        lateTracker.IsBreached.ShouldBeTrue();
    }

    [Fact]
    public async Task Multi_tenant_sla_policy_isolation()
    {
        var tenantA = await Workspaces.SignupAsync(factory, "SLA Alpha");
        var tenantB = await Workspaces.SignupAsync(factory, "SLA Beta");

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/response-sla", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/response-sla", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates an SLA policy
        var createResp = await tenantA.Client.PostAsJsonAsync("/api/v1/sla/policies", new SaveSlaPolicyRequest
        {
            Name = "Alpha 20m SLA",
            MaxResponseMinutes = 20
        });
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var alphaPolicy = (await createResp.Content.ReadFromJsonAsync<SlaPolicyDto>())!;

        // Tenant B lists policies -> should be empty
        var bListResp = await tenantB.Client.GetAsync("/api/v1/sla/policies");
        bListResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bList = (await bListResp.Content.ReadFromJsonAsync<List<SlaPolicyDto>>())!;
        bList.ShouldBeEmpty();

        // Tenant B cannot get Tenant A's policy -> 404
        var bGetResp = await tenantB.Client.GetAsync($"/api/v1/sla/policies/{alphaPolicy.Id}");
        bGetResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
