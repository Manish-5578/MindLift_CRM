using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Campaigns;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Communication;

/// <summary>Email campaigns: audience, test send, sending, tracking, unsubscribes, the daily limit and scheduling.</summary>
public sealed class CampaignTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static CampaignAudience Leads(string? tag = null) => new("leads", null, null, tag, null);

    private static async Task<LeadDto> LeadAsync(Session s, string first, string? email, string[]? tags = null)
        => (await (await s.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest(first, "Reader", email, null, "Acme", null, null, null, null, tags, null, null))).Content.ReadFromJsonAsync<LeadDto>())!;

    private static async Task<CampaignDto> MakeAsync(Session s, string name = "Autumn news", CampaignAudience? audience = null, string subject = "Hello {{first_name}}", string? body = null)
        => (await (await s.Client.PostAsJsonAsync("/api/v1/campaigns", new SaveCampaignRequest(name, subject, body ?? "Hi {{first_name}} from {{company}}.\nRead more: https://example.com/news", audience ?? Leads()))).Content.ReadFromJsonAsync<CampaignDto>())!;

    private static async Task<CampaignDto> GetAsync(Session s, Guid id) => (await s.Client.GetFromJsonAsync<CampaignDto>($"/api/v1/campaigns/{id}"))!;

    [Fact]
    public async Task The_preview_counts_who_can_be_reached_and_who_is_left_out()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var good = $"ok-{Guid.NewGuid():N}@example.test";
        var gone = $"no-{Guid.NewGuid():N}@example.test";
        await LeadAsync(admin, "Good", good);
        await LeadAsync(admin, "Gone", gone);
        await LeadAsync(admin, "Nomail", null);
        await admin.Client.PostAsJsonAsync("/api/v1/settings/email-sending/suppressions", new AddSuppressionRequest(gone));

        var preview = (await (await admin.Client.PostAsJsonAsync("/api/v1/campaigns/audience-preview", Leads())).Content.ReadFromJsonAsync<AudiencePreviewDto>())!;
        preview.Total.ShouldBe(3);
        preview.Reachable.ShouldBe(1);
        preview.NoEmail.ShouldBe(1);
        preview.DoNotEmail.ShouldBe(1);
    }

    [Fact]
    public async Task A_campaign_goes_to_the_chosen_audience_with_names_filled_in_tracking_and_an_unsubscribe_link()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var mail = factory.Services.GetRequiredService<RecordingEmailSender>();
        var tag = $"t{Guid.NewGuid():N}"[..8];
        var a = $"a-{Guid.NewGuid():N}@example.test";
        var b = $"b-{Guid.NewGuid():N}@example.test";
        var left = $"c-{Guid.NewGuid():N}@example.test";
        await LeadAsync(admin, "Alia", a, [tag]);
        await LeadAsync(admin, "Bashir", b, [tag]);
        await LeadAsync(admin, "Left", left);

        var campaign = await MakeAsync(admin, audience: Leads(tag));
        var started = (await (await admin.Client.PostAsync($"/api/v1/campaigns/{campaign.Id}/send", null)).Content.ReadFromJsonAsync<CampaignDto>())!;
        started.Status.ShouldBeOneOf("sending", "sent");

        // The test host runs background jobs at once.
        var done = await GetAsync(admin, campaign.Id);
        done.Status.ShouldBe("sent");
        done.Stats.Recipients.ShouldBe(2);
        done.Stats.Sent.ShouldBe(2);
        mail.Sent.Any(m => m.To == left).ShouldBeFalse();

        var first = mail.Sent.Last(m => m.To == a);
        first.Subject.ShouldBe("Hello Alia");
        first.Body.ShouldContain("Hi Alia from Acme.");
        first.Body.ShouldContain("/unsubscribe?t=");
        first.Html.ShouldNotBeNull();
        first.Html!.ShouldContain("/t/o/");
        first.Html.ShouldContain("/t/c/");
        first.Html.ShouldNotContain("{{");
    }

    [Fact]
    public async Task Opens_and_clicks_are_counted_and_a_click_only_goes_to_a_link_in_the_message()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var mail = factory.Services.GetRequiredService<RecordingEmailSender>();
        var address = $"track-{Guid.NewGuid():N}@example.test";
        await LeadAsync(admin, "Track", address);
        var campaign = await MakeAsync(admin);
        await admin.Client.PostAsync($"/api/v1/campaigns/{campaign.Id}/send", null);

        var html = mail.Sent.Last(m => m.To == address).Html!;
        var pixel = Regex.Match(html, @"src=""[^""]*(/t/o/[a-f0-9]+\.gif)""").Groups[1].Value;
        var click = System.Net.WebUtility.HtmlDecode(Regex.Match(html, @"href=""[^""]*(/t/c/[a-f0-9]+\?u=[^""]+)""").Groups[1].Value);
        var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });

        (await anonymous.GetAsync(pixel)).Content.Headers.ContentType!.MediaType.ShouldBe("image/gif");
        var clicked = await anonymous.GetAsync(click);
        clicked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        clicked.Headers.Location!.ToString().ShouldBe("https://example.com/news");

        // A made-up destination is refused, so the tracker cannot be used to redirect people elsewhere.
        var token = Regex.Match(click, @"/t/c/([a-f0-9]+)").Groups[1].Value;
        (await anonymous.GetAsync($"/t/c/{token}?u={Uri.EscapeDataString("https://evil.example/phish")}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var stats = (await GetAsync(admin, campaign.Id)).Stats;
        stats.Opened.ShouldBe(1);
        stats.Clicked.ShouldBe(1);
    }

    [Fact]
    public async Task Someone_who_unsubscribes_from_a_campaign_is_counted_and_never_mailed_again()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var mail = factory.Services.GetRequiredService<RecordingEmailSender>();
        var address = $"leaver-{Guid.NewGuid():N}@example.test";
        await LeadAsync(admin, "Leaver", address);
        var first = await MakeAsync(admin, "First");
        await admin.Client.PostAsync($"/api/v1/campaigns/{first.Id}/send", null);

        var link = Regex.Match(mail.Sent.Last(m => m.To == address).Body, @"/unsubscribe\?t=(\S+)").Groups[1].Value;
        (await factory.CreateClient().PostAsync($"/unsubscribe?t={link}", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(admin, first.Id)).Stats.Unsubscribed.ShouldBe(1);

        var second = await MakeAsync(admin, "Second");
        var sentBefore = mail.Sent.Count(m => m.To == address);
        (await admin.Client.PostAsync($"/api/v1/campaigns/{second.Id}/send", null)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);   // nobody left to mail
        mail.Sent.Count(m => m.To == address).ShouldBe(sentBefore);
    }

    [Fact]
    public async Task A_test_goes_to_the_address_given_and_counts_for_nothing()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var mail = factory.Services.GetRequiredService<RecordingEmailSender>();
        var campaign = await MakeAsync(admin);
        var to = $"me-{Guid.NewGuid():N}@example.test";

        (await admin.Client.PostAsJsonAsync($"/api/v1/campaigns/{campaign.Id}/test", new SendTestRequest(to))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var test = mail.Sent.Single(m => m.To == to);
        test.Subject.ShouldBe("[Test] Hello Alex");
        (await GetAsync(admin, campaign.Id)).Stats.Recipients.ShouldBe(0);
        (await admin.Client.PostAsJsonAsync($"/api/v1/campaigns/{campaign.Id}/test", new SendTestRequest("nope"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_daily_limit_pauses_a_campaign_and_the_scheduler_finishes_it_on_a_later_day()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Email:DailyLimitPerCompany", "2"));
        var admin = await Workspaces.SignupAsync(limited);
        var tag = $"t{Guid.NewGuid():N}"[..8];
        for (var i = 0; i < 4; i++) await LeadAsync(admin, $"Lim{i}", $"lim{i}-{Guid.NewGuid():N}@example.test", [tag]);

        var campaign = await MakeAsync(admin, audience: Leads(tag));
        await admin.Client.PostAsync($"/api/v1/campaigns/{campaign.Id}/send", null);

        var paused = await GetAsync(admin, campaign.Id);
        paused.Status.ShouldBe("sending");
        paused.Stats.Sent.ShouldBe(2);
        paused.Stats.Pending.ShouldBe(2);

        // A new day: yesterday's count no longer applies. Move the pause to yesterday and let the scheduler pick it up.
        using (var scope = limited.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MindLift.Infrastructure.Persistence.AppDbContext>();
            await db.Campaigns.IgnoreQueryFilters().Where(c => c.Id == campaign.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.PausedAt, DateTime.UtcNow.AddDays(-1)));
            await db.NumberSequences.Where(n => n.Key.StartsWith("mail:")).ExecuteDeleteAsync();
            await scope.ServiceProvider.GetRequiredService<CampaignScheduleJob>().RunAsync(CancellationToken.None);
        }

        var finished = await GetAsync(admin, campaign.Id);
        finished.Status.ShouldBe("sent");
        finished.Stats.Sent.ShouldBe(4);
    }

    [Fact]
    public async Task A_scheduled_campaign_starts_when_its_time_comes_and_a_started_one_cannot_be_edited()
    {
        var admin = await Workspaces.SignupAsync(factory);
        await LeadAsync(admin, "Later", $"later-{Guid.NewGuid():N}@example.test");
        var campaign = await MakeAsync(admin);

        (await admin.Client.PostAsJsonAsync($"/api/v1/campaigns/{campaign.Id}/schedule", new ScheduleCampaignRequest(DateTime.UtcNow.AddHours(-3)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var scheduled = (await (await admin.Client.PostAsJsonAsync($"/api/v1/campaigns/{campaign.Id}/schedule", new ScheduleCampaignRequest(DateTime.UtcNow.AddHours(2)))).Content.ReadFromJsonAsync<CampaignDto>())!;
        scheduled.Status.ShouldBe("scheduled");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MindLift.Infrastructure.Persistence.AppDbContext>();
            await db.Campaigns.IgnoreQueryFilters().Where(c => c.Id == campaign.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));
            await scope.ServiceProvider.GetRequiredService<CampaignScheduleJob>().RunAsync(CancellationToken.None);
        }

        (await GetAsync(admin, campaign.Id)).Status.ShouldBe("sent");
        (await admin.Client.PutAsJsonAsync($"/api/v1/campaigns/{campaign.Id}", new SaveCampaignRequest("Changed", "S", "B", Leads()))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.Client.PostAsync($"/api/v1/campaigns/{campaign.Id}/send", null)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var copy = (await (await admin.Client.PostAsync($"/api/v1/campaigns/{campaign.Id}/duplicate", null)).Content.ReadFromJsonAsync<CampaignDto>())!;
        copy.Status.ShouldBe("draft");
        copy.Name.ShouldEndWith("(copy)");
    }

    [Fact]
    public async Task Staff_can_see_campaigns_but_only_a_manager_can_send_them_and_other_companies_see_nothing()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff");
        var other = await Workspaces.SignupAsync(factory);
        var campaign = await MakeAsync(admin);

        (await staff.Client.GetAsync($"/api/v1/campaigns/{campaign.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await staff.Client.PostAsync($"/api/v1/campaigns/{campaign.Id}/send", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.PostAsJsonAsync("/api/v1/campaigns", new SaveCampaignRequest("x", "s", "b", Leads()))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await other.Client.GetAsync($"/api/v1/campaigns/{campaign.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
