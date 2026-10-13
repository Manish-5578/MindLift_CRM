using System.Net;
using System.Net.Http.Json;
using System.Text;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Activities;

/// <summary>The rest of activities: calendar invitations, @mentions in notes and the timeline of every record.</summary>
public sealed class TimelineMentionsInvitesTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static readonly string CrLf = new(new[] { (char)13, (char)10 });

    private async Task<(Session Session, PipelineDto Pipeline)> StartAsync()
    {
        var session = await Workspaces.SignupAsync(factory);
        (await session.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        var pipelines = (await session.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!;
        return (session, pipelines.First(p => p.IsDefault));
    }

    private static SaveActivityRequest Meeting(string subject, DateTime start, string[]? guests = null, bool send = false, Guid? contact = null)
        => new("Meeting", subject, "Agenda: pricing", null, start, start.AddHours(2), null, null, null, "Room 4, Dubai", null, null, contact, null, null, null, guests, send);

    private static async Task<T> SendAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<List<TimelineEntryDto>> TimelineAsync(Session s, string type, Guid id)
        => (await s.Client.GetFromJsonAsync<List<TimelineEntryDto>>($"/api/v1/timeline?entityType={type}&entityId={id}"))!;

    private IEnumerable<SentEmail> Sent(string to) => factory.Emails.Sent.Where(m => m.To.Equals(to, StringComparison.OrdinalIgnoreCase));

    // ---------- invitations ----------

    [Fact]
    public async Task A_meeting_with_guests_and_send_ticked_emails_each_guest_a_calendar_file_of_their_own()
    {
        var (s, _) = await StartAsync();
        var a = $"a-{Guid.NewGuid():N}@example.test";
        var b = $"b-{Guid.NewGuid():N}@example.test";

        var meeting = await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync("/api/v1/activities", Meeting("Pricing review, round 2", DateTime.UtcNow.AddDays(3), [a, b.ToUpperInvariant()], send: true)));

        meeting.Attendees.ShouldBe([a, b]);
        meeting.InvitationsSentAtUtc.ShouldNotBeNull();
        var mail = Sent(a).Single();
        mail.Subject.ShouldBe("Invitation: Pricing review, round 2");
        mail.Body.ShouldNotContain(b);
        var file = mail.Attachments!.Single();
        file.ContentType.ShouldContain("method=REQUEST");
        var ics = Encoding.UTF8.GetString(file.Content);
        var folded = ics;
        ics = ics.Replace(CrLf + " ", string.Empty); // Long lines continue on the next line; read them as one.
        ics.ShouldContain("BEGIN:VCALENDAR");
        ics.ShouldContain("METHOD:REQUEST");
        ics.ShouldContain($"UID:{meeting.Id}@mindlift");
        ics.ShouldContain("SUMMARY:Pricing review\\, round 2");
        ics.ShouldContain("LOCATION:Room 4\\, Dubai");
        ics.ShouldContain($"ATTENDEE;ROLE=REQ-PARTICIPANT;RSVP=TRUE:mailto:{a}");
        ics.ShouldContain($"mailto:{b}"); // A calendar file lists everyone invited, as meeting invitations normally do.
        folded.Split(CrLf).ShouldAllBe(line => System.Text.Encoding.UTF8.GetByteCount(line) <= 75);
        Sent(b).Count().ShouldBe(1);
    }

    [Fact]
    public async Task Without_send_ticked_guests_are_saved_but_nothing_is_emailed()
    {
        var (s, _) = await StartAsync();
        var guest = $"quiet-{Guid.NewGuid():N}@example.test";

        var meeting = await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync("/api/v1/activities", Meeting("Quiet meeting", DateTime.UtcNow.AddDays(1), [guest])));

        meeting.Attendees.ShouldBe([guest]);
        meeting.InvitationsSentAtUtc.ShouldBeNull();
        Sent(guest).ShouldBeEmpty();
    }

    [Fact]
    public async Task Changing_a_meeting_whose_invitations_went_out_sends_an_update_and_deleting_it_sends_a_cancellation()
    {
        var (s, _) = await StartAsync();
        var guest = $"g-{Guid.NewGuid():N}@example.test";
        var meeting = await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync("/api/v1/activities", Meeting("Kick-off", DateTime.UtcNow.AddDays(2), [guest], send: true)));

        await SendAsync<ActivityDto>(await s.Client.PutAsJsonAsync($"/api/v1/activities/{meeting.Id}", Meeting("Kick-off moved", DateTime.UtcNow.AddDays(4))));
        (await s.Client.DeleteAsync($"/api/v1/activities/{meeting.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var mails = Sent(guest).ToList();
        mails.Select(m => m.Subject).ShouldBe(["Invitation: Kick-off", "Updated: Kick-off moved", "Cancelled: Kick-off moved"]);
        Encoding.UTF8.GetString(mails[1].Attachments!.Single().Content).ShouldContain("SEQUENCE:");
        var cancel = Encoding.UTF8.GetString(mails[2].Attachments!.Single().Content);
        cancel.ShouldContain("METHOD:CANCEL");
        cancel.ShouldContain("STATUS:CANCELLED");
    }

    [Fact]
    public async Task Invitations_need_a_start_time_and_guests_and_every_address_must_be_valid()
    {
        var (s, _) = await StartAsync();

        (await s.Client.PostAsJsonAsync("/api/v1/activities", Meeting("No guests", DateTime.UtcNow.AddDays(1), [], send: true))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", Meeting("Bad guest", DateTime.UtcNow.AddDays(1), ["not-an-email"]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", Meeting("No start", DateTime.UtcNow, ["x@example.test"], send: true) with { DueAt = null, EndAt = null })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Task", "Not a meeting", null, null, null, null, null, null, null, null, null, null, null, null, null, null, ["x@example.test"]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", Meeting("Too many", DateTime.UtcNow.AddDays(1), Enumerable.Range(0, 51).Select(i => $"g{i}@example.test").ToArray()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_subject_with_line_breaks_cannot_inject_calendar_lines()
    {
        var (s, _) = await StartAsync();
        var guest = $"safe-{Guid.NewGuid():N}@example.test";

        await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync("/api/v1/activities", Meeting("Hello\r\nATTENDEE:mailto:evil@example.test", DateTime.UtcNow.AddDays(1), [guest], send: true)));

        var ics = Encoding.UTF8.GetString(Sent(guest).Single().Attachments!.Single().Content);
        ics.ShouldNotContain("\r\nATTENDEE:mailto:evil@example.test");
        ics.ShouldContain("SUMMARY:Hello\\nATTENDEE:mailto:evil@example.test");
    }

    // ---------- mentions ----------

    [Fact]
    public async Task A_note_can_mention_active_users_and_the_mentions_can_be_changed()
    {
        var (s, _) = await StartAsync();
        var me = (await s.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;
        var note = new SaveActivityRequest("Note", "Heads up", "Please look at this", null, null, null, null, null, null, null, null, null, null, null, null, null, null, false, [me.UserId]);

        var created = await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync("/api/v1/activities", note));
        created.Mentions.Select(m => m.UserId).ShouldBe([me.UserId]);
        created.Mentions[0].Name.ShouldNotBeNullOrWhiteSpace();

        var cleared = await SendAsync<ActivityDto>(await s.Client.PutAsJsonAsync($"/api/v1/activities/{created.Id}", note with { MentionUserIds = [] }));
        cleared.Mentions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Mentions_are_for_notes_and_only_for_real_active_users()
    {
        var (s, _) = await StartAsync();
        var other = await Workspaces.SignupAsync(factory);
        var stranger = (await other.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.UserId;
        var note = new SaveActivityRequest("Note", "x", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false, [stranger]);

        (await s.Client.PostAsJsonAsync("/api/v1/activities", note)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", note with { MentionUserIds = [Guid.NewGuid()] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", note with { Type = "Task", MentionUserIds = [stranger] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- timeline ----------

    [Fact]
    public async Task A_deals_timeline_shows_its_creation_changes_stage_moves_and_activities_newest_first()
    {
        var (s, pipeline) = await StartAsync();
        var deal = await SendAsync<DealDto>(await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Timeline deal", null, null, 100, null, null, null, null, null, null, null, null)));
        await s.Client.PutAsJsonAsync($"/api/v1/deals/{deal.Id}", new SaveDealRequest("Timeline deal", null, null, 250, null, null, null, null, null, null, null, null));
        await s.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(pipeline.Stages.Where(x => x.StageType == "Open").OrderBy(x => x.SortOrder).ElementAt(1).Id, null, null, null, null));
        await s.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Note", "Call went well", "They want a quote", null, null, null, null, null, null, null, null, null, null, null, deal.Id, null));

        var entries = await TimelineAsync(s, "deals", deal.Id);

        entries.Select(e => e.Kind).Distinct().Order().ToList().ShouldBe(["activity", "changed", "created", "stage"]);
        entries.ShouldBe(entries.OrderByDescending(e => e.WhenUtc).ToList());
        entries.Single(e => e.Kind == "activity").Title.ShouldBe("Note: Call went well");
        entries.Single(e => e.Kind == "stage").Title.ShouldStartWith("Moved from ");
        var change = entries.Single(e => e.Kind == "changed" && e.Title == "Amount changed");
        change.Detail.ShouldNotBeNull();
        change.Detail!.ShouldContain("→");
        entries.ShouldAllBe(e => e.ActorName == null || e.ActorName.Length > 0);
        entries.Where(e => e.Kind is "created" or "changed" or "stage").ShouldAllBe(e => e.ActorName != null);
    }

    [Fact]
    public async Task A_contact_converted_from_a_lead_shows_that_history_and_the_leads_activities()
    {
        var (s, _) = await StartAsync();
        var lead = await SendAsync<LeadDto>(await s.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Hist", "Lead", "h@example.test", null, "Hist Co", "new", "website", null, null, null, null, null)));
        await s.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Call", "First call", null, null, null, null, null, "Outbound", null, null, null, lead.Id, null, null, null, null));
        var result = await SendAsync<ConvertLeadResult>(await s.Client.PostAsJsonAsync($"/api/v1/leads/{lead.Id}/convert",
            new ConvertLeadRequest("create", null, null, "new", null, false, null, null, null, null, null, null)));

        var entries = await TimelineAsync(s, "contacts", result.ContactId);

        entries.ShouldContain(e => e.Kind == "converted" && e.Title == "Converted from lead Hist Lead" && e.Href == $"/leads/{lead.Id}");
        entries.ShouldContain(e => e.Kind == "activity" && e.Title == "Call done: First call");
    }

    [Fact]
    public async Task An_accounts_timeline_gathers_its_contacts_deals_and_their_activities()
    {
        var (s, _) = await StartAsync();
        var account = await SendAsync<AccountDto>(await s.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Gather Co", null, null, null, null, null, null, null)));
        var contact = await SendAsync<ContactDto>(await s.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Pat", "Tester", null, null, null, null, account.Id, null, null, null)));
        var deal = await SendAsync<DealDto>(await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Gather deal", null, null, 900, null, null, null, account.Id, null, null, null, null)));
        await s.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Note", "On the contact", null, null, null, null, null, null, null, null, null, null, contact.Id, null, null, null));
        await s.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Note", "On the deal", null, null, null, null, null, null, null, null, null, null, null, null, deal.Id, null));

        var titles = (await TimelineAsync(s, "accounts", account.Id)).Select(e => e.Title).ToList();

        titles.ShouldContain("Contact added: Pat Tester");
        titles.ShouldContain("Deal created: Gather deal");
        titles.ShouldContain("Note: On the contact");
        titles.ShouldContain("Note: On the deal");
    }

    [Fact]
    public async Task The_timeline_refuses_unknown_kinds_and_records_of_other_workspaces()
    {
        var (s, _) = await StartAsync();
        var other = await Workspaces.SignupAsync(factory);
        var contact = await SendAsync<ContactDto>(await other.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Hidden", "Person", null, null, null, null, null, null, null, null)));

        (await s.Client.GetAsync($"/api/v1/timeline?entityType=invoices&entityId={Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.GetAsync($"/api/v1/timeline?entityType=contacts&entityId={contact.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Client.GetAsync($"/api/v1/timeline?entityType=contacts&entityId={Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/timeline?entityType=contacts&entityId={contact.Id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
