using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Communication;

public sealed class EmailTrackingTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(string first, string last, string email)
        => new(first, last, email, null, null, "new", "website", null, null, null, null, null);

    private static SaveContactRequest NewContact(string first, string last, string email)
        => new(first, last, email, null, null, null, null, null, null, null);

    [Fact]
    public async Task Returns_404_when_email_tracking_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var emailsResp = await admin.Client.GetAsync("/api/v1/email-tracking/emails");
        emailsResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var settingsResp = await admin.Client.GetAsync("/api/v1/email-tracking/settings");
        settingsResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var statsResp = await admin.Client.GetAsync("/api/v1/email-tracking/stats");
        statsResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Settings_retrieval_and_update()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Get default settings
        var getResp = await admin.Client.GetAsync("/api/v1/email-tracking/settings");
        getResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = await getResp.Content.ReadFromJsonAsync<EmailTrackingSettingsDto>();

        settings.ShouldNotBeNull();
        settings.TrackOpens.ShouldBeTrue();
        settings.TrackClicks.ShouldBeTrue();
        settings.NotifyOnFirstOpen.ShouldBeTrue();

        // 2. Update settings
        var updateReq = new SaveEmailTrackingSettingsRequest
        {
            TrackOpens = true,
            TrackClicks = true,
            NotifyOnFirstOpen = true,
            NotifyOnLinkClick = false,
            CustomTrackingDomain = "track.acme.corp"
        };
        var putResp = await admin.Client.PutAsJsonAsync("/api/v1/email-tracking/settings", updateReq);
        putResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await putResp.Content.ReadFromJsonAsync<EmailTrackingSettingsDto>();

        updated.ShouldNotBeNull();
        updated.NotifyOnLinkClick.ShouldBeFalse();
        updated.CustomTrackingDomain.ShouldBe("track.acme.corp");
    }

    [Fact]
    public async Task Email_registration_and_html_transformation()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var htmlInput = """
            <html>
            <body>
                <h1>Hello</h1>
                <p>Please check our <a href="https://pricing.acme.com/quote">Special Pricing Offer</a> for details.</p>
                <p>Also visit <a href="https://docs.acme.com/help">Documentation</a>.</p>
            </body>
            </html>
            """;

        var regReq = new RegisterTrackedEmailRequest
        {
            RecipientEmail = "prospect@targetcompany.com",
            Subject = "Exclusive Q4 Enterprise Proposal",
            SenderEmail = "account.exec@acme.corp",
            HtmlBody = htmlInput
        };

        var regResp = await admin.Client.PostAsJsonAsync("/api/v1/email-tracking/register", regReq);
        regResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var regResult = await regResp.Content.ReadFromJsonAsync<RegisterTrackedEmailResult>();

        regResult.ShouldNotBeNull();
        regResult.LinksRewrittenCount.ShouldBe(2);
        regResult.TrackingToken.ShouldNotBeNullOrWhiteSpace();

        // Verify HTML contains tracking pixel and rewritten click links
        regResult.ProcessedHtmlBody.ShouldContain("/api/v1/email-tracking/click/");
        regResult.ProcessedHtmlBody.ShouldContain($"/api/v1/email-tracking/pixel/{regResult.TrackingToken}");
        regResult.ProcessedHtmlBody.ShouldNotContain("https://pricing.acme.com/quote"); // Rewritten
    }

    [Fact]
    public async Task Open_tracking_pixel_triggers_metrics_and_first_open_timeline_alert()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create a lead
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Thomas", "Anderson", "neo@matrix.io"));
        leadResp.EnsureSuccessStatusCode();
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // 2. Register tracked email for this lead
        var regReq = new RegisterTrackedEmailRequest
        {
            RecipientEmail = "neo@matrix.io",
            Subject = "Wake up, Neo",
            SenderEmail = "morpheus@nebuchadnezzar.ship",
            HtmlBody = "<html><body><p>Follow the white rabbit.</p></body></html>",
            LeadId = lead.Id
        };
        var regResp = await admin.Client.PostAsJsonAsync("/api/v1/email-tracking/register", regReq);
        regResp.EnsureSuccessStatusCode();
        var regResult = await regResp.Content.ReadFromJsonAsync<RegisterTrackedEmailResult>();
        regResult.ShouldNotBeNull();

        // 3. Anonymous HTTP client opens the pixel
        var anonClient = factory.CreateClient();
        var pixelResp = await anonClient.GetAsync($"/api/v1/email-tracking/pixel/{regResult.TrackingToken}");

        pixelResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        pixelResp.Content.Headers.ContentType?.MediaType.ShouldBe("image/gif");
        var gifBytes = await pixelResp.Content.ReadAsByteArrayAsync();
        gifBytes.Length.ShouldBe(43); // 1x1 transparent GIF

        // 4. Hit pixel a second time (multiple opens)
        await anonClient.GetAsync($"/api/v1/email-tracking/pixel/{regResult.TrackingToken}");

        // 5. Verify tracked email metrics
        var getEmailResp = await admin.Client.GetAsync($"/api/v1/email-tracking/emails/{regResult.TrackedEmailId}");
        getEmailResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var email = await getEmailResp.Content.ReadFromJsonAsync<TrackedEmailDto>();

        email.ShouldNotBeNull();
        email.OpenCount.ShouldBe(2);
        email.UniqueOpenCount.ShouldBe(1);
        email.FirstOpenedAtUtc.ShouldNotBeNull();
        email.LastOpenedAtUtc.ShouldNotBeNull();
        email.LeadId.ShouldBe(lead.Id);

        // 6. Verify open events logged
        var eventsResp = await admin.Client.GetAsync($"/api/v1/email-tracking/emails/{regResult.TrackedEmailId}/events");
        eventsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var events = await eventsResp.Content.ReadFromJsonAsync<List<TrackedEmailEventDto>>();

        events.ShouldNotBeNull();
        events.Count.ShouldBe(2);
        events.ShouldAllBe(e => e.EventType == "Open");
    }

    [Fact]
    public async Task Click_tracking_redirects_and_updates_metrics()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create a contact
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Trinity", "Matrix", "trinity@matrix.io"));
        contactResp.EnsureSuccessStatusCode();
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();

        // 2. Register email with destination link
        var targetUrl = "https://matrix.io/rendezvous-point";
        var regReq = new RegisterTrackedEmailRequest
        {
            RecipientEmail = "trinity@matrix.io",
            Subject = "Meeting coordinates",
            HtmlBody = $"<html><body><a href=\"{targetUrl}\">Coordinates Link</a></body></html>",
            ContactId = contact.Id
        };
        var regResp = await admin.Client.PostAsJsonAsync("/api/v1/email-tracking/register", regReq);
        regResp.EnsureSuccessStatusCode();
        var regResult = await regResp.Content.ReadFromJsonAsync<RegisterTrackedEmailResult>();
        regResult.ShouldNotBeNull();

        // Extract link token from processed HTML
        var match = Regex.Match(regResult.ProcessedHtmlBody, @"/api/v1/email-tracking/click/(?<token>[a-zA-Z0-9]+)");
        match.Success.ShouldBeTrue();
        var linkToken = match.Groups["token"].Value;

        // 3. Anonymous HTTP client clicks the link without following redirect automatically
        var anonClient = factory.CreateClient(new() { AllowAutoRedirect = false });

        var clickResp = await anonClient.GetAsync($"/api/v1/email-tracking/click/{linkToken}");
        clickResp.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        clickResp.Headers.Location?.ToString().ShouldBe(targetUrl);

        // 4. Verify email click counters
        var getEmailResp = await admin.Client.GetAsync($"/api/v1/email-tracking/emails/{regResult.TrackedEmailId}");
        getEmailResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var email = await getEmailResp.Content.ReadFromJsonAsync<TrackedEmailDto>();

        email.ShouldNotBeNull();
        email.ClickCount.ShouldBe(1);
        email.UniqueClickCount.ShouldBe(1);
        email.FirstClickedAtUtc.ShouldNotBeNull();

        // 5. Verify click event logged
        var eventsResp = await admin.Client.GetAsync($"/api/v1/email-tracking/emails/{regResult.TrackedEmailId}/events");
        eventsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var events = await eventsResp.Content.ReadFromJsonAsync<List<TrackedEmailEventDto>>();

        events.ShouldNotBeNull();
        events.ShouldContain(e => e.EventType == "Click" && e.Url == targetUrl);
    }

    [Fact]
    public async Task Engagement_stats_aggregation()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Register 2 emails
        var reg1 = await admin.Client.PostAsJsonAsync("/api/v1/email-tracking/register", new RegisterTrackedEmailRequest
        {
            RecipientEmail = "user1@demo.com",
            Subject = "Demo 1",
            HtmlBody = "<html><body><a href=\"https://demo.com\">Link</a></body></html>"
        });
        reg1.EnsureSuccessStatusCode();
        var res1 = await reg1.Content.ReadFromJsonAsync<RegisterTrackedEmailResult>();
        res1.ShouldNotBeNull();

        var reg2 = await admin.Client.PostAsJsonAsync("/api/v1/email-tracking/register", new RegisterTrackedEmailRequest
        {
            RecipientEmail = "user2@demo.com",
            Subject = "Demo 2",
            HtmlBody = "<html><body>Plain text</body></html>"
        });
        reg2.EnsureSuccessStatusCode();

        // Open email 1
        var anonClient = factory.CreateClient();
        await anonClient.GetAsync($"/api/v1/email-tracking/pixel/{res1.TrackingToken}");

        // Click email 1 link
        var match = Regex.Match(res1.ProcessedHtmlBody, @"/api/v1/email-tracking/click/(?<token>[a-zA-Z0-9]+)");
        match.Success.ShouldBeTrue();
        var anonNoRedirect = factory.CreateClient(new() { AllowAutoRedirect = false });
        await anonNoRedirect.GetAsync($"/api/v1/email-tracking/click/{match.Groups["token"].Value}");

        // Verify stats
        var statsResp = await admin.Client.GetAsync("/api/v1/email-tracking/stats");
        statsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stats = await statsResp.Content.ReadFromJsonAsync<EmailTrackingStatsDto>();

        stats.ShouldNotBeNull();
        stats.TotalSent.ShouldBe(2);
        stats.TotalOpens.ShouldBe(1);
        stats.UniqueOpens.ShouldBe(1);
        stats.OpenRatePercent.ShouldBe(50.0);
        stats.TotalClicks.ShouldBe(1);
        stats.UniqueClicks.ShouldBe(1);
        stats.ClickRatePercent.ShouldBe(50.0);
    }

    [Fact]
    public async Task Strict_multi_tenant_isolation_fails_closed()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/email-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/email-tracking", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A registers email
        var regResp = await tenantA.Client.PostAsJsonAsync("/api/v1/email-tracking/register", new RegisterTrackedEmailRequest
        {
            RecipientEmail = "sensitive@client.com",
            Subject = "Tenant A Sensitive Info",
            HtmlBody = "<html><body>Confidential data</body></html>"
        });
        regResp.EnsureSuccessStatusCode();
        var regA = await regResp.Content.ReadFromJsonAsync<RegisterTrackedEmailResult>();
        regA.ShouldNotBeNull();

        // Tenant B attempts to fetch Tenant A's email -> 404
        var crossGet = await tenantB.Client.GetAsync($"/api/v1/email-tracking/emails/{regA.TrackedEmailId}");
        crossGet.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to fetch Tenant A's events -> empty
        var crossEvents = await tenantB.Client.GetAsync($"/api/v1/email-tracking/emails/{regA.TrackedEmailId}/events");
        crossEvents.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eventsB = await crossEvents.Content.ReadFromJsonAsync<List<TrackedEmailEventDto>>();
        eventsB.ShouldNotBeNull();
        eventsB.ShouldBeEmpty();

        // Tenant B lists emails -> empty
        var listB = await tenantB.Client.GetAsync("/api/v1/email-tracking/emails");
        listB.StatusCode.ShouldBe(HttpStatusCode.OK);
        var emailsB = await listB.Content.ReadFromJsonAsync<List<TrackedEmailDto>>();
        emailsB.ShouldNotBeNull();
        emailsB.ShouldBeEmpty();

        // Tenant B stats -> 0
        var statsB = await (await tenantB.Client.GetAsync("/api/v1/email-tracking/stats")).Content.ReadFromJsonAsync<EmailTrackingStatsDto>();
        statsB.ShouldNotBeNull();
        statsB.TotalSent.ShouldBe(0);
        statsB.TotalOpens.ShouldBe(0);
    }
}
