using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Notifications;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Tenancy;

/// <summary>
/// A workspace that has lapsed (PastDue, Cancelled, Suspended, or a trial that has ended) can still be read, but takes nothing new:
/// not staff writes, not public bookings or website forms, not provider webhooks, and its reminders are not sent.
/// </summary>
public sealed class LapsedWorkspaceTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveContactRequest NewContact(string email)
        => new("Mona", "Synthetic", email, "+971500000001", null, null, null, "client", null, null);

    private async Task<Guid> TenantIdAsync(Session session) => (await session.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;

    private async Task SetStandingAsync(Session session, string status, DateTime? trialEndsAt = null)
    {
        var tenantId = await TenantIdAsync(session);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Tenants.Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, status).SetProperty(t => t.TrialEndsAt, trialEndsAt));
    }

    // ---------- staff ----------

    [Fact]
    public async Task A_trial_that_has_ended_refuses_writes_straight_away_and_still_allows_reads()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await SetStandingAsync(owner, "Trial", DateTime.UtcNow.AddMinutes(-5));

        var write = await owner.Client.PostAsJsonAsync("/api/v1/contacts", NewContact($"late-{Guid.NewGuid():N}@example.test"));
        var read = await owner.Client.GetAsync("/api/v1/users?pageSize=1");

        write.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_trial_that_is_still_running_accepts_writes()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await SetStandingAsync(owner, "Trial", DateTime.UtcNow.AddDays(5));

        (await owner.Client.PostAsJsonAsync("/api/v1/contacts", NewContact($"ok-{Guid.NewGuid():N}@example.test"))).IsSuccessStatusCode.ShouldBeTrue();
    }

    // ---------- public booking ----------

    private static SaveBookingPageRequest BookingPage(string slug, Guid userId)
    {
        var hours = new AvailabilityDto("UTC", new Dictionary<string, IReadOnlyList<TimeRangeDto>>
        {
            ["mon"] = [new("09:00", "17:00")], ["tue"] = [new("09:00", "17:00")], ["wed"] = [new("09:00", "17:00")],
            ["thu"] = [new("09:00", "17:00")], ["fri"] = [new("09:00", "17:00")], ["sat"] = [], ["sun"] = [],
        }, []);
        return new(slug, "Discovery call", "A short call", 30, 0, 0, 30, 1, 30, "SingleUser", [userId], hours,
            [new("company", "Company", false, "text")], "Video", "https://meet.example.test/room", [60, 15], true);
    }

    [Theory]
    [InlineData("PastDue")]
    [InlineData("Cancelled")]
    [InlineData("Suspended")]
    public async Task A_lapsed_workspaces_booking_page_is_not_offered_and_comes_back_when_it_is_active_again(string status)
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = (await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;
        var created = await owner.Client.PostAsJsonAsync("/api/v1/settings/booking-pages", BookingPage("intro", me.UserId));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var page = (await created.Content.ReadFromJsonAsync<BookingPageDto>())!;
        var parts = page.PublicPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var url = $"/api/v1/public/booking/{parts[1]}/{parts[2]}";
        var visitor = factory.CreateClient();
        (await visitor.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetStandingAsync(owner, status);
        (await visitor.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SetStandingAsync(owner, "Active");
        (await visitor.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- website forms ----------

    [Fact]
    public async Task A_lapsed_workspaces_website_form_takes_no_leads()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var form = (await (await owner.Client.PostAsJsonAsync("/api/v1/settings/web-forms",
            new SaveWebFormRequest("Contact us", "website", "Thanks!", true, ["first_name", "email", "phone"]))).Content.ReadFromJsonAsync<WebFormDto>())!;
        var visitor = factory.CreateClient();
        PublicFormSubmission Submission() => new("Web", "Visitor", $"web-{Guid.NewGuid():N}@example.test", null, null, "Hello", null);

        await SetStandingAsync(owner, "PastDue");
        (await visitor.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}", Submission())).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SetStandingAsync(owner, "Active");
        (await visitor.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}", Submission())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- provider webhooks ----------

    [Fact]
    public async Task A_genuine_webhook_for_a_lapsed_workspace_is_acknowledged_and_nothing_is_stored()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var number = "1" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999);
        (await owner.Client.PostAsJsonAsync("/api/v1/channels", new SaveChannelAccountRequest(
            CommunicationChannel.WhatsApp, "WhatsApp", "+" + number, IsDefault: true, IsActive: true, AutoCreateLeads: true,
            PhoneNumberId: number, WebhookSecret: "lapsed-test-secret"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await SetStandingAsync(owner, "PastDue");

        var response = await factory.CreateClient().SendAsync(WebhookRequests.WhatsApp(
            WebhookRequests.InboundWhatsApp(number, "+" + number, "+15550401", "wamid.lapsed", "hello"), "lapsed-test-secret"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.Client.GetFromJsonAsync<List<InboxThreadDto>>("/api/v1/communication/inbox?channel=WhatsApp"))!.ShouldBeEmpty();

        await SetStandingAsync(owner, "Active");
        (await factory.CreateClient().SendAsync(WebhookRequests.WhatsApp(
            WebhookRequests.InboundWhatsApp(number, "+" + number, "+15550402", "wamid.active", "hello"), "lapsed-test-secret"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.Client.GetFromJsonAsync<List<InboxThreadDto>>("/api/v1/communication/inbox?channel=WhatsApp"))!.ShouldContain(t => t.ParticipantAddress == "+15550402");
    }

    // ---------- customer portal ----------

    private const string PortalPassword = "Customer!Pass1";

    private async Task<(Session Admin, string Slug, string Email)> PortalCustomerAsync()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var me = await admin.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        string slug;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            slug = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == me!.TenantId).Select(t => t.Slug).SingleAsync();
        }

        var email = $"portal-{Guid.NewGuid():N}@example.test";
        var contact = (await (await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact(email))).Content.ReadFromJsonAsync<ContactDto>())!;
        (await admin.Client.PostAsJsonAsync($"/api/v1/contacts/{contact.Id}/portal/invite", new InvitePortalUserRequest(contact.Id, email))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var accept = await factory.CreateClient().PostAsJsonAsync($"/api/v1/portal/{slug}/auth/accept-invite",
            new AcceptPortalInviteRequest(factory.Emails.LatestTokenFor(email), PortalPassword));
        accept.StatusCode.ShouldBe(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        return (admin, slug, email);
    }

    [Fact]
    public async Task A_suspended_workspaces_portal_cannot_be_signed_into_but_a_past_due_one_still_can()
    {
        var (admin, slug, email) = await PortalCustomerAsync();
        var customer = factory.CreateClient();
        Task<HttpResponseMessage> SignIn() => customer.PostAsJsonAsync($"/api/v1/portal/{slug}/auth/login", new PortalLoginRequest(email, PortalPassword));

        await SetStandingAsync(admin, "PastDue");
        (await SignIn()).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetStandingAsync(admin, "Suspended");
        var refused = await SignIn();
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("portal_disabled");
    }

    // ---------- background jobs ----------

    [Fact]
    public async Task The_overdue_task_job_skips_a_lapsed_workspace_and_still_serves_the_others()
    {
        var lapsed = await Workspaces.SignupAsync(factory);
        var active = await Workspaces.SignupAsync(factory);
        foreach (var session in new[] { lapsed, active })
        {
            var created = await session.Client.PostAsJsonAsync("/api/v1/activities",
                new SaveActivityRequest("Task", "Late task", null, null, DateTime.UtcNow.AddHours(-3), null, null, null, null, null, null, null, null, null, null, null));
            created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        }

        await SetStandingAsync(lapsed, "PastDue");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OverdueTaskJob>().RunAsync();
        }

        async Task<int> OverdueCountAsync(Session s)
            => (await s.Client.GetFromJsonAsync<NotificationPageDto>("/api/v1/notifications?unreadOnly=false&page=1&pageSize=20"))!.Items.Count(n => n.Type == NotificationTypes.TaskOverdue);

        (await OverdueCountAsync(active)).ShouldBe(1);
        (await OverdueCountAsync(lapsed)).ShouldBe(0);
    }
}
