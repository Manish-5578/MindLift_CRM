using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Automation;

public sealed class VisitorTrackingTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(string first, string last, string email)
        => new(first, last, email, null, "Atlantis Tech", "new", "website", null, null, null, null, null);

    [Fact]
    public async Task Returns_404_when_visitor_tracking_feature_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var req = new SaveWebsiteTrackerRequest
        {
            Name = "Disabled Tracker",
            Domain = "example.com"
        };

        var postResp = await admin.Client.PostAsJsonAsync("/api/v1/visitor-tracking/trackers", req);
        postResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var listResp = await admin.Client.GetAsync("/api/v1/visitor-tracking/trackers");
        listResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var analyticsResp = await admin.Client.GetAsync("/api/v1/visitor-tracking/analytics");
        analyticsResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task End_to_end_tracker_crud_and_snippet_generation()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/visitor-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create Tracker
        var createReq = new SaveWebsiteTrackerRequest
        {
            Name = "Main SaaS Portal",
            Domain = "mindlift.io",
            AutoCreateLeads = true,
            AllowedOrigins = ["https://mindlift.io", "https://app.mindlift.io"]
        };

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/visitor-tracking/trackers", createReq);
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var tracker = await createResp.Content.ReadFromJsonAsync<WebsiteTrackerDto>();

        tracker.ShouldNotBeNull();
        tracker.Name.ShouldBe("Main SaaS Portal");
        tracker.Domain.ShouldBe("mindlift.io");
        tracker.TrackingToken.ShouldStartWith("trk_");
        tracker.EmbedSnippet.ShouldContain(tracker.TrackingToken);

        // 2. Get by Id
        var getResp = await admin.Client.GetAsync($"/api/v1/visitor-tracking/trackers/{tracker.Id}");
        getResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var retrieved = await getResp.Content.ReadFromJsonAsync<WebsiteTrackerDto>();
        retrieved.ShouldNotBeNull();
        retrieved.Id.ShouldBe(tracker.Id);

        // 3. Update Tracker
        createReq.Name = "Global Marketing & Docs";
        var updateResp = await admin.Client.PutAsJsonAsync($"/api/v1/visitor-tracking/trackers/{tracker.Id}", createReq);
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<WebsiteTrackerDto>();
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("Global Marketing & Docs");

        // 4. List Trackers
        var listResp = await admin.Client.GetAsync("/api/v1/visitor-tracking/trackers");
        listResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await listResp.Content.ReadFromJsonAsync<List<WebsiteTrackerDto>>();
        list.ShouldNotBeNull();
        list.Count.ShouldBeGreaterThanOrEqualTo(1);

        // 5. Delete Tracker
        var deleteResp = await admin.Client.DeleteAsync($"/api/v1/visitor-tracking/trackers/{tracker.Id}");
        deleteResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getAfterDelete = await admin.Client.GetAsync($"/api/v1/visitor-tracking/trackers/{tracker.Id}");
        getAfterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_visitor_hit_recording_and_session_aggregation()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/visitor-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Create tracker
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/visitor-tracking/trackers", new SaveWebsiteTrackerRequest
        {
            Name = "Docs Site",
            Domain = "docs.mindlift.io"
        });
        createResp.EnsureSuccessStatusCode();
        var tracker = (await createResp.Content.ReadFromJsonAsync<WebsiteTrackerDto>())!;

        var anonClient = factory.CreateClient();

        // Anonymous hit 1: Homepage
        var hit1 = await anonClient.PostAsJsonAsync("/api/v1/visitor-tracking/hit", new TrackHitRequest
        {
            TrackingToken = tracker.TrackingToken,
            VisitorId = "v_test_anon_1",
            SessionId = "s_test_anon_1",
            Url = "https://docs.mindlift.io/welcome",
            Title = "MindLift Documentation",
            DurationSeconds = 12
        });
        hit1.StatusCode.ShouldBe(HttpStatusCode.OK);
        var res1 = await hit1.Content.ReadFromJsonAsync<TrackHitResultDto>();
        res1.ShouldNotBeNull();
        res1.Success.ShouldBeTrue();
        res1.Identified.ShouldBeFalse();

        // Anonymous hit 2: Pricing page in same session
        var hit2 = await anonClient.PostAsJsonAsync("/api/v1/visitor-tracking/hit", new TrackHitRequest
        {
            TrackingToken = tracker.TrackingToken,
            VisitorId = "v_test_anon_1",
            SessionId = "s_test_anon_1",
            Url = "https://docs.mindlift.io/pricing",
            Title = "Plans & Pricing",
            DurationSeconds = 45
        });
        hit2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var res2 = await hit2.Content.ReadFromJsonAsync<TrackHitResultDto>();
        res2.ShouldNotBeNull();
        res2.Success.ShouldBeTrue();

        // Admin queries sessions
        var sessionsResp = await admin.Client.GetAsync($"/api/v1/visitor-tracking/sessions?trackerId={tracker.Id}");
        sessionsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sessions = await sessionsResp.Content.ReadFromJsonAsync<List<VisitorSessionDto>>();
        sessions.ShouldNotBeNull();
        sessions.Count.ShouldBeGreaterThanOrEqualTo(1);

        var session = sessions.Single(s => s.VisitorId == "v_test_anon_1");
        session.PageViewsCount.ShouldBe(2);
        session.TotalDurationSeconds.ShouldBe(45);
        session.PageViews.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Visitor_identification_links_past_and_future_sessions_to_lead()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/visitor-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Pre-create lead
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Arthur", "Curry", "arthur@atlantis.test"));
        leadResp.EnsureSuccessStatusCode();
        var lead = (await leadResp.Content.ReadFromJsonAsync<LeadDto>())!;

        // Create tracker
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/visitor-tracking/trackers", new SaveWebsiteTrackerRequest
        {
            Name = "Marketing Site",
            Domain = "atlantis.io"
        });
        var tracker = (await createResp.Content.ReadFromJsonAsync<WebsiteTrackerDto>())!;

        var anonClient = factory.CreateClient();

        // 1. First browse anonymously
        var hit1 = await anonClient.PostAsJsonAsync("/api/v1/visitor-tracking/hit", new TrackHitRequest
        {
            TrackingToken = tracker.TrackingToken,
            VisitorId = "v_aquaman_77",
            SessionId = "s_aquaman_1",
            Url = "https://atlantis.io/solutions",
            Title = "Oceanic Solutions",
            DurationSeconds = 30
        });
        hit1.EnsureSuccessStatusCode();

        // 2. Visitor identifies on a contact form with their email
        var identifyResp = await anonClient.PostAsJsonAsync("/api/v1/visitor-tracking/identify", new IdentifyVisitorRequest
        {
            TrackingToken = tracker.TrackingToken,
            VisitorId = "v_aquaman_77",
            Email = "arthur@atlantis.test"
        });
        identifyResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var identResult = await identifyResp.Content.ReadFromJsonAsync<TrackHitResultDto>();
        identResult.ShouldNotBeNull();
        identResult.Success.ShouldBeTrue();
        identResult.Identified.ShouldBeTrue();
        identResult.AssociatedLeadId.ShouldBe(lead.Id);

        // 3. Admin checks sessions for this lead
        var leadSessionsResp = await admin.Client.GetAsync($"/api/v1/visitor-tracking/sessions?leadId={lead.Id}");
        leadSessionsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var leadSessions = await leadSessionsResp.Content.ReadFromJsonAsync<List<VisitorSessionDto>>();
        leadSessions.ShouldNotBeNull();
        leadSessions.Count.ShouldBeGreaterThanOrEqualTo(1);
        leadSessions[0].IdentifiedEmail.ShouldBe("arthur@atlantis.test");
        leadSessions[0].LeadId.ShouldBe(lead.Id);
    }

    [Fact]
    public async Task Visitor_analytics_summary_computes_aggregations()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/visitor-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/visitor-tracking/trackers", new SaveWebsiteTrackerRequest
        {
            Name = "Analytics Test Site",
            Domain = "testsite.com"
        });
        var tracker = (await createResp.Content.ReadFromJsonAsync<WebsiteTrackerDto>())!;

        var anonClient = factory.CreateClient();

        await anonClient.PostAsJsonAsync("/api/v1/visitor-tracking/hit", new TrackHitRequest
        {
            TrackingToken = tracker.TrackingToken,
            VisitorId = "v_stat_1",
            SessionId = "s_stat_1",
            Url = "https://testsite.com/home",
            Title = "Home",
            DurationSeconds = 20
        });

        await anonClient.PostAsJsonAsync("/api/v1/visitor-tracking/hit", new TrackHitRequest
        {
            TrackingToken = tracker.TrackingToken,
            VisitorId = "v_stat_2",
            SessionId = "s_stat_2",
            Url = "https://testsite.com/home",
            Title = "Home",
            DurationSeconds = 40
        });

        var analyticsResp = await admin.Client.GetAsync($"/api/v1/visitor-tracking/analytics?trackerId={tracker.Id}");
        analyticsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var analytics = await analyticsResp.Content.ReadFromJsonAsync<VisitorAnalyticsDto>();

        analytics.ShouldNotBeNull();
        analytics.TotalVisitors.ShouldBe(2);
        analytics.TotalPageViews.ShouldBe(2);
        analytics.ActiveNowCount.ShouldBe(2);
        analytics.TopPages.ShouldContain(tp => tp.Url == "https://testsite.com/home" && tp.ViewsCount == 2);
    }

    [Fact]
    public async Task Multi_tenant_visitor_tracking_isolation()
    {
        var tenantA = await Workspaces.SignupAsync(factory, "Tracking Alpha");
        var tenantB = await Workspaces.SignupAsync(factory, "Tracking Beta");

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/visitor-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/visitor-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var createResp = await tenantA.Client.PostAsJsonAsync("/api/v1/visitor-tracking/trackers", new SaveWebsiteTrackerRequest
        {
            Name = "Alpha Tracker",
            Domain = "alpha.com"
        });
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var alphaTracker = (await createResp.Content.ReadFromJsonAsync<WebsiteTrackerDto>())!;

        // Tenant B lists trackers -> should be empty
        var bListResp = await tenantB.Client.GetAsync("/api/v1/visitor-tracking/trackers");
        bListResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bList = (await bListResp.Content.ReadFromJsonAsync<List<WebsiteTrackerDto>>())!;
        bList.ShouldBeEmpty();

        // Tenant B cannot get Tenant A's tracker -> 404
        var bGetResp = await tenantB.Client.GetAsync($"/api/v1/visitor-tracking/trackers/{alphaTracker.Id}");
        bGetResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
