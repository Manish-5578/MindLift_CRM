using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Notifications;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Notifications;

/// <summary>Notifications (plan 7.11): who is told, in the app and by email, the reminder jobs, preferences and real-time delivery.</summary>
public sealed class NotificationTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private sealed record Pair(Session Admin, Guid AdminId, Session Staff, Guid StaffId);

    private async Task<Pair> PairAsync()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "notify");
        return new Pair(admin, await IdAsync(admin), staff, await IdAsync(staff));
    }

    private static async Task<Guid> IdAsync(Session s) => (await s.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.UserId;

    private static async Task<NotificationPageDto> ListAsync(Session s, bool unreadOnly = false, int page = 1, int pageSize = 20)
        => (await s.Client.GetFromJsonAsync<NotificationPageDto>($"/api/v1/notifications?unreadOnly={unreadOnly}&page={page}&pageSize={pageSize}"))!;

    private static async Task<int> UnreadAsync(Session s) => (await s.Client.GetFromJsonAsync<UnreadCountDto>("/api/v1/notifications/unread-count"))!.Unread;

    private static async Task<T> SendAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static Task<LeadDto> NewLeadAsync(Session s, string first, Guid? owner = null)
        => s.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest(first, "Lead", null, null, "Acme", "new", "website", null, null, null, owner, null)).ContinueWith(t => SendAsync<LeadDto>(t.Result)).Unwrap();

    private static SaveActivityRequest Task(string subject, DateTime? due = null, int? reminder = null, Guid? owner = null, string type = "Task")
        => new(type, subject, null, null, due, null, null, null, null, null, reminder, null, null, null, null, owner);

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    private async Task WaitForEmailAsync(string to, string subjectContains, int expected = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            if (factory.Emails.Sent.Count(m => m.To.Equals(to, StringComparison.OrdinalIgnoreCase) && m.Subject.Contains(subjectContains)) >= expected)
            {
                return;
            }

            await System.Threading.Tasks.Task.Delay(200);
        }

        // Say what the background queue was doing, so a timeout explains itself.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM hangfire.server";
        var servers = Convert.ToString(await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COALESCE(string_agg(state || '=' || n, ', '), 'none') FROM (SELECT state, count(*) AS n FROM hangfire.state GROUP BY state) s";
        var states = Convert.ToString(await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COALESCE(string_agg(left(invocationdata::text, 60) || ' ' || coalesce(statename, ''), ' | '), 'no jobs') FROM (SELECT invocationdata, statename FROM hangfire.job WHERE statename IS DISTINCT FROM 'Succeeded' LIMIT 3) x";
        var detail = Convert.ToString(await command.ExecuteScalarAsync());
        throw new TimeoutException($"No email to {to} ('{subjectContains}') within 40 seconds. Hangfire servers: {servers}. Hangfire states: {states}. Not succeeded: {detail}");
    }

    private int EmailCount(string to, string subjectContains) => factory.Emails.Sent.Count(m => m.To.Equals(to, StringComparison.OrdinalIgnoreCase) && m.Subject.Contains(subjectContains));

    // ---------- assignment ----------

    [Fact]
    public async Task Giving_a_lead_to_a_colleague_tells_them_and_not_the_person_who_did_it()
    {
        var p = await PairAsync();

        var lead = await NewLeadAsync(p.Admin, "Handed", p.StaffId);

        var mine = await ListAsync(p.Staff);
        mine.Items.Count.ShouldBe(1);
        mine.Unread.ShouldBe(1);
        mine.Items[0].Type.ShouldBe(NotificationTypes.LeadAssigned);
        mine.Items[0].Title.ShouldBe("Lead assigned to you: Handed Lead");
        mine.Items[0].Url.ShouldBe($"/leads/{lead.Id}");
        mine.Items[0].IsRead.ShouldBeFalse();
        (await ListAsync(p.Admin)).Total.ShouldBe(0);
    }

    [Fact]
    public async Task Taking_a_lead_for_yourself_or_changing_nothing_sends_nothing()
    {
        var p = await PairAsync();

        var lead = await NewLeadAsync(p.Admin, "Own", p.AdminId);
        await p.Admin.Client.PutAsJsonAsync($"/api/v1/leads/{lead.Id}", new SaveLeadRequest("Own", "Lead", null, null, "Acme", "working", "website", null, null, null, null, null));

        (await ListAsync(p.Admin)).Total.ShouldBe(0);
        (await ListAsync(p.Staff)).Total.ShouldBe(0);
    }

    [Fact]
    public async Task Reassigning_a_lead_tells_the_new_owner_once()
    {
        var p = await PairAsync();
        var lead = await NewLeadAsync(p.Admin, "Move", p.AdminId);
        var save = new SaveLeadRequest("Move", "Lead", null, null, "Acme", "new", "website", null, null, null, p.StaffId, null);

        await p.Admin.Client.PutAsJsonAsync($"/api/v1/leads/{lead.Id}", save);
        await p.Admin.Client.PutAsJsonAsync($"/api/v1/leads/{lead.Id}", save with { Description = "edited again" });

        (await ListAsync(p.Staff)).Total.ShouldBe(1);
    }

    [Fact]
    public async Task Contacts_accounts_deals_and_tasks_given_to_someone_tell_them()
    {
        var p = await PairAsync();
        await SendAsync<ContactDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Pat", "Tester", null, null, null, null, null, null, p.StaffId, null)));
        await SendAsync<AccountDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Gift Co", null, null, null, null, null, p.StaffId, null)));
        (await p.Admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        await SendAsync<DealDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Gift deal", null, null, 100, null, null, null, null, null, null, p.StaffId, null)));
        await SendAsync<ActivityDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/activities", Task("Call them", DateTime.UtcNow.AddDays(1), owner: p.StaffId)));

        var types = (await ListAsync(p.Staff)).Items.Select(n => n.Type).Order().ToList();

        types.ShouldBe([NotificationTypes.AccountAssigned, NotificationTypes.ContactAssigned, NotificationTypes.DealAssigned, NotificationTypes.TaskAssigned]);
    }

    [Fact]
    public async Task A_deactivated_person_is_not_notified()
    {
        var p = await PairAsync();
        (await p.Admin.Client.PostAsync($"/api/v1/users/{p.StaffId}/deactivate", null)).EnsureSuccessStatusCode();

        await NewLeadAsync(p.Admin, "Nobody home", p.StaffId);

        await InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            (await db.Notifications.IgnoreQueryFilters().CountAsync(n => n.UserId == p.StaffId)).ShouldBe(0);
            return 0;
        });
    }

    [Fact]
    public async Task Winning_or_losing_a_deal_tells_its_owner_but_not_the_person_who_moved_it()
    {
        var p = await PairAsync();
        (await p.Admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        var pipeline = (await p.Admin.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!.First(x => x.IsDefault);
        var won = await SendAsync<DealDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("To win", null, null, 500, null, null, null, null, null, null, p.StaffId, null)));
        var lost = await SendAsync<DealDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("To lose", null, null, 500, null, null, null, null, null, null, p.StaffId, null)));

        await p.Admin.Client.PatchAsJsonAsync($"/api/v1/deals/{won.Id}/stage", new ChangeDealStageRequest(pipeline.Stages.First(s => s.StageType == "Won").Id, null, null, null, null));
        await p.Admin.Client.PatchAsJsonAsync($"/api/v1/deals/{lost.Id}/stage", new ChangeDealStageRequest(pipeline.Stages.First(s => s.StageType == "Lost").Id, "price", null, null, null));

        var types = (await ListAsync(p.Staff)).Items.Select(n => n.Type).ToList();
        types.ShouldContain(NotificationTypes.DealWon);
        types.ShouldContain(NotificationTypes.DealLost);
        (await ListAsync(p.Admin)).Items.Select(n => n.Type).ShouldNotContain(NotificationTypes.DealWon);
    }

    [Fact]
    public async Task A_note_that_mentions_someone_tells_them_once_even_if_the_note_is_edited()
    {
        var p = await PairAsync();
        var note = new SaveActivityRequest("Note", "Look at this", "Please check", null, null, null, null, null, null, null, null, null, null, null, null, null, null, false, [p.StaffId]);
        var created = await SendAsync<ActivityDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/activities", note));

        await p.Admin.Client.PutAsJsonAsync($"/api/v1/activities/{created.Id}", note with { Description = "Please check again" });

        var mine = (await ListAsync(p.Staff)).Items;
        mine.Count.ShouldBe(1);
        mine[0].Type.ShouldBe(NotificationTypes.Mentioned);
        mine[0].Title.ShouldContain("mentioned you");
    }

    [Fact]
    public async Task Finishing_an_import_tells_the_person_who_ran_it()
    {
        var p = await PairAsync();

        var response = await p.Admin.Client.PostAsync("/api/v1/leads/import", new StringContent("First name,Email\nImp,imp@example.test\n", System.Text.Encoding.UTF8, "text/csv"));
        response.EnsureSuccessStatusCode();

        var mine = (await ListAsync(p.Admin)).Items;
        mine.Single(n => n.Type == NotificationTypes.ImportCompleted).Body.ShouldBe("1 imported, 0 skipped, 0 could not be imported.");
    }

    // ---------- reading ----------

    [Fact]
    public async Task Notifications_can_be_filtered_paged_marked_read_one_by_one_or_all_at_once()
    {
        var p = await PairAsync();
        for (var i = 1; i <= 5; i++)
        {
            await NewLeadAsync(p.Admin, $"Lead{i}", p.StaffId);
        }

        (await UnreadAsync(p.Staff)).ShouldBe(5);
        var firstPage = await ListAsync(p.Staff, pageSize: 2);
        firstPage.Items.Count.ShouldBe(2);
        firstPage.Total.ShouldBe(5);
        firstPage.Items[0].Title.ShouldContain("Lead5");

        (await p.Staff.Client.PostAsync($"/api/v1/notifications/{firstPage.Items[0].Id}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await UnreadAsync(p.Staff)).ShouldBe(4);
        (await ListAsync(p.Staff, unreadOnly: true)).Total.ShouldBe(4);

        (await p.Staff.Client.PostAsync("/api/v1/notifications/read-all", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await UnreadAsync(p.Staff)).ShouldBe(0);
        (await ListAsync(p.Staff)).Total.ShouldBe(5);
    }

    [Fact]
    public async Task Nobody_can_read_or_mark_someone_elses_notifications()
    {
        var p = await PairAsync();
        await NewLeadAsync(p.Admin, "Private", p.StaffId);
        var theirs = (await ListAsync(p.Staff)).Items.Single();
        var stranger = await Workspaces.SignupAsync(factory);

        (await p.Admin.Client.PostAsync($"/api/v1/notifications/{theirs.Id}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.Client.PostAsync($"/api/v1/notifications/{theirs.Id}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(stranger)).Total.ShouldBe(0);
        (await UnreadAsync(p.Staff)).ShouldBe(1);
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/notifications")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- preferences and email ----------

    [Fact]
    public async Task Preferences_start_with_the_defaults_and_can_be_changed_and_are_checked()
    {
        var p = await PairAsync();

        var defaults = await p.Staff.Client.GetFromJsonAsync<NotificationPreferencesDto>("/api/v1/notifications/preferences");
        defaults!.Preferences.Count.ShouldBe(NotificationTypes.All.Count);
        defaults.Preferences.ShouldAllBe(x => x.InApp);
        defaults.Preferences.Single(x => x.Type == NotificationTypes.LeadAssigned).Email.ShouldBeTrue();
        defaults.Preferences.Single(x => x.Type == NotificationTypes.DealWon).Email.ShouldBeFalse();
        defaults.DailyDigest.ShouldBeFalse();

        var changed = defaults.Preferences.Select(x => x.Type == NotificationTypes.DealWon ? x with { Email = true } : x).ToList();
        var saved = await SendAsync<NotificationPreferencesDto>(await p.Staff.Client.PutAsJsonAsync("/api/v1/notifications/preferences", new SaveNotificationPreferencesRequest(changed, true)));
        saved.DailyDigest.ShouldBeTrue();
        saved.Preferences.Single(x => x.Type == NotificationTypes.DealWon).Email.ShouldBeTrue();

        (await p.Staff.Client.PutAsJsonAsync("/api/v1/notifications/preferences",
            new SaveNotificationPreferencesRequest([new NotificationPreferenceDto("Nonsense", "x", true, true)], false))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await p.Admin.Client.GetFromJsonAsync<NotificationPreferencesDto>("/api/v1/notifications/preferences"))!.DailyDigest.ShouldBeFalse();
    }

    [Fact]
    public async Task Turning_the_in_app_copy_off_hides_the_notification_but_still_counts_as_sent_by_email()
    {
        var p = await PairAsync();
        var prefs = (await p.Staff.Client.GetFromJsonAsync<NotificationPreferencesDto>("/api/v1/notifications/preferences"))!;
        await p.Staff.Client.PutAsJsonAsync("/api/v1/notifications/preferences", new SaveNotificationPreferencesRequest(
            prefs.Preferences.Select(x => x.Type == NotificationTypes.LeadAssigned ? x with { InApp = false } : x).ToList(), false));

        await NewLeadAsync(p.Admin, "Quiet", p.StaffId);

        (await UnreadAsync(p.Staff)).ShouldBe(0);
        await WaitForEmailAsync(p.Staff.Email, "Lead assigned to you: Quiet Lead");
        EmailCount(p.Staff.Email, "Lead assigned to you: Quiet Lead").ShouldBe(1);
    }

    [Fact]
    public async Task An_assignment_email_arrives_by_default_and_not_when_switched_off()
    {
        var p = await PairAsync();
        await NewLeadAsync(p.Admin, "Emailed", p.StaffId);
        await WaitForEmailAsync(p.Staff.Email, "Lead assigned to you: Emailed Lead");
        var mail = factory.Emails.Sent.Single(m => m.To.Equals(p.Staff.Email, StringComparison.OrdinalIgnoreCase) && m.Subject.Contains("Emailed Lead"));
        mail.Body.ShouldContain("/leads/");
        mail.Body.ShouldContain("Notification preferences");

        var prefs = (await p.Staff.Client.GetFromJsonAsync<NotificationPreferencesDto>("/api/v1/notifications/preferences"))!;
        await p.Staff.Client.PutAsJsonAsync("/api/v1/notifications/preferences", new SaveNotificationPreferencesRequest(
            prefs.Preferences.Select(x => x.Type == NotificationTypes.LeadAssigned ? x with { Email = false } : x).ToList(), false));
        await NewLeadAsync(p.Admin, "Silent", p.StaffId);
        await System.Threading.Tasks.Task.Delay(1500);

        EmailCount(p.Staff.Email, "Silent Lead").ShouldBe(0);
        (await ListAsync(p.Staff)).Items.Select(n => n.Title).ShouldContain("Lead assigned to you: Silent Lead");
    }

    // ---------- reminders ----------

    [Fact]
    public async Task An_overdue_task_tells_its_owner_once_and_a_burst_shares_one_email()
    {
        var p = await PairAsync();
        for (var i = 1; i <= 3; i++)
        {
            await SendAsync<ActivityDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/activities", Task($"Late {i}", DateTime.UtcNow.AddHours(-i), owner: p.StaffId)));
        }

        var done = await SendAsync<ActivityDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/activities", Task("Finished", DateTime.UtcNow.AddHours(-9), owner: p.StaffId)));
        await p.Staff.Client.PostAsJsonAsync($"/api/v1/activities/{done.Id}/complete", new CompleteActivityRequest(null, null, null));
        await SendAsync<ActivityDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/activities", Task("Not yet", DateTime.UtcNow.AddDays(2), owner: p.StaffId)));

        await InScopeAsync(async sp => await sp.GetRequiredService<OverdueTaskJob>().RunAsync());
        var again = await InScopeAsync(async sp => await sp.GetRequiredService<OverdueTaskJob>().RunAsync());

        var overdue = (await ListAsync(p.Staff)).Items.Where(n => n.Type == NotificationTypes.TaskOverdue).ToList();
        overdue.Count.ShouldBe(3);
        overdue.ShouldAllBe(n => n.Title.StartsWith("Task overdue: Late"));
        again.ShouldBe(0);
        await WaitForEmailAsync(p.Staff.Email, "Task overdue");
        await System.Threading.Tasks.Task.Delay(1500);
        EmailCount(p.Staff.Email, "Task overdue").ShouldBe(1);
    }

    [Fact]
    public async Task A_reminder_goes_out_at_the_chosen_time_once_and_again_if_the_task_is_moved()
    {
        var p = await PairAsync();
        var soon = await SendAsync<ActivityDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/activities", Task("Reminder due", DateTime.UtcNow.AddMinutes(30), reminder: 60, owner: p.StaffId)));
        await SendAsync<ActivityDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/activities", Task("Too early", DateTime.UtcNow.AddMinutes(30), reminder: 10, owner: p.StaffId)));
        await SendAsync<ActivityDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/activities", Task("Standup", DateTime.UtcNow.AddMinutes(20), reminder: 30, owner: p.StaffId, type: "Meeting")));

        await InScopeAsync(async sp => await sp.GetRequiredService<ActivityReminderJob>().RunAsync());
        await InScopeAsync(async sp => await sp.GetRequiredService<ActivityReminderJob>().RunAsync());

        var titles = (await ListAsync(p.Staff)).Items.Where(n => n.Type is NotificationTypes.TaskDueSoon or NotificationTypes.MeetingReminder).Select(n => n.Title).Order().ToList();
        titles.ShouldBe(["Meeting soon: Standup", "Task due soon: Reminder due"]);

        // Moving the task gives it a new reminder.
        await p.Admin.Client.PutAsJsonAsync($"/api/v1/activities/{soon.Id}", Task("Reminder due", DateTime.UtcNow.AddMinutes(45), reminder: 60, owner: p.StaffId));
        await InScopeAsync(async sp => await sp.GetRequiredService<ActivityReminderJob>().RunAsync());
        (await ListAsync(p.Staff)).Items.Count(n => n.Title == "Task due soon: Reminder due").ShouldBe(2);
    }

    [Fact]
    public async Task A_completed_task_gets_no_reminder()
    {
        var p = await PairAsync();
        var task = await SendAsync<ActivityDto>(await p.Admin.Client.PostAsJsonAsync("/api/v1/activities", Task("Already done", DateTime.UtcNow.AddMinutes(20), reminder: 30, owner: p.StaffId)));
        await p.Staff.Client.PostAsJsonAsync($"/api/v1/activities/{task.Id}/complete", new CompleteActivityRequest(null, null, null));

        await InScopeAsync(async sp => await sp.GetRequiredService<ActivityReminderJob>().RunAsync());

        (await ListAsync(p.Staff)).Items.Select(n => n.Type).ShouldNotContain(NotificationTypes.TaskDueSoon);
    }

    [Fact]
    public async Task The_daily_digest_goes_out_once_at_eight_local_time_to_people_who_asked_and_has_something_in_it()
    {
        var p = await PairAsync();
        var me = (await p.Staff.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;
        var zone = TimeZoneInfo.GetSystemTimeZones().First(z => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, z).Hour == DailyDigestJob.SendHour);
        await InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<AppDbContext>().TenantSettings.IgnoreQueryFilters().Where(t => t.TenantId == me.TenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.TimeZoneId, zone.Id));
            return 0;
        });
        var prefs = (await p.Staff.Client.GetFromJsonAsync<NotificationPreferencesDto>("/api/v1/notifications/preferences"))!;

        // Nothing is asked for yet, so nothing goes out.
        await p.Admin.Client.PostAsJsonAsync("/api/v1/activities", Task("Today thing", DateTime.UtcNow, owner: p.StaffId));
        await InScopeAsync(async sp => await sp.GetRequiredService<DailyDigestJob>().RunAsync());
        EmailCount(p.Staff.Email, "Your day").ShouldBe(0);

        await p.Staff.Client.PutAsJsonAsync("/api/v1/notifications/preferences", new SaveNotificationPreferencesRequest(prefs.Preferences, true));
        await InScopeAsync(async sp => await sp.GetRequiredService<DailyDigestJob>().RunAsync());
        await InScopeAsync(async sp => await sp.GetRequiredService<DailyDigestJob>().RunAsync());

        EmailCount(p.Staff.Email, "Your day").ShouldBe(1);
        var mail = factory.Emails.Sent.Single(m => m.To.Equals(p.Staff.Email, StringComparison.OrdinalIgnoreCase) && m.Subject.Contains("Your day"));
        mail.Body.ShouldContain("Today thing");
    }

    [Fact]
    public async Task The_daily_digest_is_not_sent_when_the_day_is_empty()
    {
        var p = await PairAsync();
        var me = (await p.Staff.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;
        var zone = TimeZoneInfo.GetSystemTimeZones().First(z => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, z).Hour == DailyDigestJob.SendHour);
        await InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<AppDbContext>().TenantSettings.IgnoreQueryFilters().Where(t => t.TenantId == me.TenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.TimeZoneId, zone.Id));
            return 0;
        });
        var prefs = (await p.Staff.Client.GetFromJsonAsync<NotificationPreferencesDto>("/api/v1/notifications/preferences"))!;
        await p.Staff.Client.PutAsJsonAsync("/api/v1/notifications/preferences", new SaveNotificationPreferencesRequest(prefs.Preferences, true));

        await InScopeAsync(async sp => await sp.GetRequiredService<DailyDigestJob>().RunAsync());

        EmailCount(p.Staff.Email, "Your day").ShouldBe(0);
    }

    [Fact]
    public async Task Old_notifications_are_cleaned_up_and_recent_ones_stay()
    {
        var p = await PairAsync();
        await NewLeadAsync(p.Admin, "Old", p.StaffId);
        await NewLeadAsync(p.Admin, "Recent", p.StaffId);
        await InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("UPDATE notifications SET created_at = now() - interval '200 days' WHERE title LIKE '%Old Lead%' AND user_id = {0}", p.StaffId);
            return 0;
        });

        await InScopeAsync(async sp => await sp.GetRequiredService<NotificationCleanupJob>().RunAsync());

        (await ListAsync(p.Staff)).Items.Select(n => n.Title).ShouldBe(["Lead assigned to you: Recent Lead"]);
    }

    // ---------- real time ----------

    private HubConnection Connect(string? token)
        => new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/notifications"), options =>
            {
                options.AccessTokenProvider = () => System.Threading.Tasks.Task.FromResult(token);
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();

    [Fact]
    public async Task A_signed_in_person_receives_a_new_notification_immediately_and_nobody_else_does()
    {
        var p = await PairAsync();
        var bystander = await Workspaces.InviteAndAcceptAsync(factory, p.Admin, "Staff", "bystander");
        var received = new TaskCompletionSource<(NotificationDto Notification, int Unread)>();
        var overheard = new List<NotificationDto>();
        await using var theirs = Connect(p.Staff.AccessToken);
        await using var other = Connect(bystander.AccessToken);
        theirs.On<NotificationDto, int>("ReceiveNotification", (n, unread) => received.TrySetResult((n, unread)));
        other.On<NotificationDto, int>("ReceiveNotification", (n, _) => overheard.Add(n));
        await theirs.StartAsync();
        await other.StartAsync();

        await NewLeadAsync(p.Admin, "Live", p.StaffId);

        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        message.Notification.Title.ShouldBe("Lead assigned to you: Live Lead");
        message.Unread.ShouldBe(1);
        await System.Threading.Tasks.Task.Delay(500);
        overheard.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_hub_refuses_a_connection_with_no_valid_token()
    {
        await using var anonymous = Connect(null);
        await Should.ThrowAsync<Exception>(() => anonymous.StartAsync());

        await using var forged = Connect("not-a-token");
        await Should.ThrowAsync<Exception>(() => forged.StartAsync());
    }
}
