using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Activities;

/// <summary>Activities and onboarding checklists: rules, links, tabs, completing, follow-ups and the deal-won trigger.</summary>
public sealed class ActivityTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private async Task<(Session Session, PipelineDto Pipeline)> StartAsync()
    {
        var session = await Workspaces.SignupAsync(factory);
        (await session.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        var pipelines = (await session.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!;
        return (session, pipelines.First(p => p.IsDefault));
    }

    private static SaveActivityRequest Task(string subject, DateTime? due = null, Guid? deal = null, Guid? contact = null, Guid? account = null, Guid? lead = null)
        => new("Task", subject, null, null, due, null, null, null, null, null, null, lead, contact, account, deal, null);

    private static async Task<T> SendAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<ActivityDto> CreateAsync(Session s, SaveActivityRequest request) => await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync("/api/v1/activities", request));

    private static async Task<PagedResult<ActivityDto>> ListAsync(Session s, ActivityQuery query) => await SendAsync<PagedResult<ActivityDto>>(await s.Client.PostAsJsonAsync("/api/v1/activities/query", query));

    private static ActivityQuery Tab(string? tab, Guid? deal = null) => new(tab, null, null, false, null, null, null, deal);

    [Fact]
    public async Task A_task_starts_open_and_a_note_and_a_logged_call_start_completed()
    {
        var (s, _) = await StartAsync();

        var task = await CreateAsync(s, Task("Send quote", DateTime.UtcNow.AddDays(2)));
        var note = await CreateAsync(s, new("Note", "Met on site", "Liked the demo", null, null, null, null, null, null, null, null, null, null, null, null, null));
        var call = await CreateAsync(s, new("Call", "Intro call", null, null, null, null, 15, "Outbound", "connected", null, null, null, null, null, null, null));

        task.Status.ShouldBe("Open");
        note.Status.ShouldBe("Completed");
        call.Status.ShouldBe("Completed");
        call.Direction.ShouldBe("Outbound");
        call.CallOutcome.ShouldBe("connected");
    }

    [Fact]
    public async Task The_rules_for_each_field_are_enforced()
    {
        var (s, _) = await StartAsync();

        (await s.Client.PostAsJsonAsync("/api/v1/activities", Task(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", Task(new string('x', 251)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", Task("x") with { Type = "Email" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", Task("x") with { Priority = "Urgent" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", Task("x") with { Direction = "Inbound" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", Task("x") with { CallOutcome = "connected" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", Task("x") with { DurationMinutes = -1 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", Task("x", deal: Guid.NewGuid()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Meeting", "Demo", null, null, DateTime.UtcNow.AddDays(1), DateTime.UtcNow, null, null, null, null, null, null, null, null, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Call", "Bad outcome", null, null, null, null, null, null, "nonsense", null, null, null, null, null, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Linking_a_deal_brings_its_company_and_main_contact_and_a_contact_brings_its_company()
    {
        var (s, _) = await StartAsync();
        var account = await SendAsync<AccountDto>(await s.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Acme", null, null, null, null, null, null, null)));
        var contact = await SendAsync<ContactDto>(await s.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Pat", "Tester", null, null, null, null, account.Id, null, null, null)));
        var deal = await SendAsync<DealDto>(await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Big", null, null, 100, null, null, null, null, [new(contact.Id, null, true)], null, null, null)));

        var viaDeal = await CreateAsync(s, Task("Follow up", deal: deal.Id));
        var viaContact = await CreateAsync(s, Task("Call back", contact: contact.Id));

        viaDeal.AccountId.ShouldBe(account.Id);
        viaDeal.ContactId.ShouldBe(contact.Id);
        viaDeal.LinkedKind.ShouldBe("deal");
        viaContact.AccountId.ShouldBe(account.Id);
    }

    [Fact]
    public async Task The_tabs_split_open_work_into_overdue_today_and_upcoming()
    {
        var (s, _) = await StartAsync();
        await CreateAsync(s, Task("Overdue", DateTime.UtcNow.AddDays(-3)));
        await CreateAsync(s, Task("Today", DateTime.UtcNow));
        await CreateAsync(s, Task("Soon", DateTime.UtcNow.AddDays(5)));
        await CreateAsync(s, Task("Someday"));
        var done = await CreateAsync(s, Task("Done", DateTime.UtcNow.AddDays(-1)));
        await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync($"/api/v1/activities/{done.Id}/complete", new CompleteActivityRequest(null, null, null)));

        (await ListAsync(s, Tab("overdue"))).Items.Select(a => a.Subject).ShouldBe(["Overdue"]);
        (await ListAsync(s, Tab("today"))).Items.Select(a => a.Subject).ShouldContain("Today");
        (await ListAsync(s, Tab("upcoming"))).Items.Select(a => a.Subject).ToList().ShouldBe(["Soon", "Someday"]);
        (await ListAsync(s, Tab("completed"))).Items.Select(a => a.Subject).ShouldBe(["Done"]);
        (await ListAsync(s, Tab("overdue"))).Items[0].IsOverdue.ShouldBeTrue();

        var day = await s.Client.GetFromJsonAsync<MyDayDto>("/api/v1/activities/my-day");
        day!.Overdue.ShouldBe(1);
        day.Upcoming.ShouldBe(1);
    }

    [Fact]
    public async Task Completing_a_task_can_create_a_follow_up_that_keeps_the_links()
    {
        var (s, _) = await StartAsync();
        var contact = await SendAsync<ContactDto>(await s.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Pat", "Tester", null, null, null, null, null, null, null, null)));
        var task = await CreateAsync(s, Task("Send brochure", DateTime.UtcNow, contact: contact.Id));

        var completed = await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync($"/api/v1/activities/{task.Id}/complete",
            new CompleteActivityRequest(null, "Sent by email", Task("Check they got it", DateTime.UtcNow.AddDays(3)))));

        completed.Status.ShouldBe("Completed");
        completed.CompletedAtUtc.ShouldNotBeNull();
        completed.Description.ShouldBe("Sent by email");
        var open = (await ListAsync(s, Tab("upcoming"))).Items.Single();
        open.Subject.ShouldBe("Check they got it");
        open.ContactId.ShouldBe(contact.Id);
        open.ParentActivityId.ShouldBe(task.Id);
        (await s.Client.PostAsJsonAsync($"/api/v1/activities/{task.Id}/complete", new CompleteActivityRequest(null, null, null))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_completed_task_can_be_reopened_but_a_note_cannot()
    {
        var (s, _) = await StartAsync();
        var task = await CreateAsync(s, Task("Reopen me", DateTime.UtcNow));
        await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync($"/api/v1/activities/{task.Id}/complete", new CompleteActivityRequest(null, null, null)));

        var reopened = await SendAsync<ActivityDto>(await s.Client.PostAsync($"/api/v1/activities/{task.Id}/reopen", null));

        reopened.Status.ShouldBe("Open");
        reopened.CompletedAtUtc.ShouldBeNull();
        var note = await CreateAsync(s, new("Note", "A note", null, null, null, null, null, null, null, null, null, null, null, null, null, null));
        (await s.Client.PostAsync($"/api/v1/activities/{note.Id}/reopen", null)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_call_records_its_outcome_when_completed_but_a_task_cannot()
    {
        var (s, _) = await StartAsync();
        var call = await CreateAsync(s, new("Call", "Planned call", null, null, DateTime.UtcNow.AddHours(3), null, null, "Outbound", null, null, null, null, null, null, null, null));
        var task = await CreateAsync(s, Task("Not a call"));

        (await s.Client.PostAsJsonAsync($"/api/v1/activities/{task.Id}/complete", new CompleteActivityRequest("connected", null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync($"/api/v1/activities/{call.Id}/complete", new CompleteActivityRequest("nonsense", null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync($"/api/v1/activities/{call.Id}/complete", new CompleteActivityRequest("connected", null, null)))).CallOutcome.ShouldBe("connected");
    }

    [Fact]
    public async Task A_deals_next_and_last_activity_follow_its_tasks()
    {
        var (s, _) = await StartAsync();
        var deal = await SendAsync<DealDto>(await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Timed", null, null, 1, null, null, null, null, null, null, null, null)));
        var task = await CreateAsync(s, Task("Next step", DateTime.UtcNow.AddDays(2), deal: deal.Id));

        await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync($"/api/v1/activities/{task.Id}/complete", new CompleteActivityRequest(null, null, null)));
        await s.Client.PostAsJsonAsync("/api/v1/activities", Task("Later step", DateTime.UtcNow.AddDays(9), deal: deal.Id));

        var board = await SendAsync<DealBoardDto>(await s.Client.PostAsJsonAsync("/api/v1/deals/board", new { }));
        board.Stages.SelectMany(c => c.Deals).Single(d => d.Id == deal.Id).DaysInStage.ShouldBe(0);
        (await s.Client.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal.Id}"))!.IsRotting.ShouldBeFalse();
    }

    [Fact]
    public async Task A_lead_conversion_moves_the_leads_activities_to_the_new_records()
    {
        var (s, _) = await StartAsync();
        var lead = await SendAsync<LeadDto>(await s.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Conv", "Lead", "c@example.test", null, "Conv Co", "new", "website", null, null, null, null, null)));
        await CreateAsync(s, Task("Call the lead", DateTime.UtcNow.AddDays(1), lead: lead.Id));

        var result = await SendAsync<ConvertLeadResult>(await s.Client.PostAsJsonAsync($"/api/v1/leads/{lead.Id}/convert",
            new ConvertLeadRequest("create", null, null, "new", null, true, null, null, null, 10, null, null)));

        var onContact = await ListAsync(s, new ActivityQuery(null, null, null, false, null, result.ContactId, null, null));
        onContact.Items.Select(a => a.Subject).ShouldBe(["Call the lead"]);
        (await ListAsync(s, new ActivityQuery(null, null, null, false, null, null, null, result.DealId))).Total.ShouldBe(1);
        (await ListAsync(s, new ActivityQuery(null, null, null, false, null, null, result.AccountId, null))).Total.ShouldBe(1);
    }

    [Fact]
    public async Task Winning_a_deal_creates_the_tasks_of_each_matching_checklist_once_and_shows_progress()
    {
        var (s, pipeline) = await StartAsync();
        var other = await SendAsync<PipelineDto>(await s.Client.PostAsJsonAsync("/api/v1/settings/pipelines", new SavePipelineRequest("Other", null, false, true, 9)));
        await SendAsync<ChecklistTemplateDto>(await s.Client.PostAsJsonAsync("/api/v1/settings/checklists",
            new SaveChecklistTemplateRequest("Welcome", null, true, [new("Send contract", 1, null), new("Kick-off call", 7, "Book the team")])));
        await SendAsync<ChecklistTemplateDto>(await s.Client.PostAsJsonAsync("/api/v1/settings/checklists",
            new SaveChecklistTemplateRequest("Other pipeline only", other.Id, true, [new("Never created", 0, null)])));
        await SendAsync<ChecklistTemplateDto>(await s.Client.PostAsJsonAsync("/api/v1/settings/checklists",
            new SaveChecklistTemplateRequest("Switched off", null, false, [new("Also never", 0, null)])));
        var deal = await SendAsync<DealDto>(await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Won deal", null, null, 100, null, null, null, null, null, null, null, null)));
        var won = pipeline.Stages.First(x => x.StageType == "Won");
        var open = pipeline.Stages.First(x => x.StageType == "Open");

        await SendAsync<DealDto>(await s.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(won.Id, null, null, null, null)));
        var progress = (await s.Client.GetFromJsonAsync<List<OnboardingProgressDto>>($"/api/v1/activities/onboarding/{deal.Id}"))!;

        progress.Count.ShouldBe(1);
        progress[0].TemplateName.ShouldBe("Welcome");
        progress[0].Total.ShouldBe(2);
        progress[0].Completed.ShouldBe(0);
        progress[0].Tasks.Select(t => t.Subject).ShouldBe(["Send contract", "Kick-off call"]);
        progress[0].Tasks[1].DueAtUtc!.Value.Date.ShouldBe(DateTime.UtcNow.Date.AddDays(7));

        await SendAsync<ActivityDto>(await s.Client.PostAsJsonAsync($"/api/v1/activities/{progress[0].Tasks[0].Id}/complete", new CompleteActivityRequest(null, null, null)));
        (await s.Client.GetFromJsonAsync<List<OnboardingProgressDto>>($"/api/v1/activities/onboarding/{deal.Id}"))![0].Completed.ShouldBe(1);

        // Moving it out of Won and back in does not create the tasks a second time.
        await SendAsync<DealDto>(await s.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(open.Id, null, null, null, null)));
        await SendAsync<DealDto>(await s.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(won.Id, null, null, null, null)));
        (await s.Client.GetFromJsonAsync<List<OnboardingProgressDto>>($"/api/v1/activities/onboarding/{deal.Id}"))![0].Total.ShouldBe(2);
    }

    [Fact]
    public async Task Checklist_templates_are_validated_and_names_are_unique()
    {
        var (s, _) = await StartAsync();

        (await s.Client.PostAsJsonAsync("/api/v1/settings/checklists", new SaveChecklistTemplateRequest(" ", null, true, [new("x", 0, null)]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/settings/checklists", new SaveChecklistTemplateRequest("Empty", null, true, []))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/settings/checklists", new SaveChecklistTemplateRequest("Bad days", null, true, [new("x", 400, null)]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/settings/checklists", new SaveChecklistTemplateRequest("No subject", null, true, [new(" ", 1, null)]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await SendAsync<ChecklistTemplateDto>(await s.Client.PostAsJsonAsync("/api/v1/settings/checklists", new SaveChecklistTemplateRequest("Same", null, true, [new("x", 0, null)])));
        (await s.Client.PostAsJsonAsync("/api/v1/settings/checklists", new SaveChecklistTemplateRequest("same", null, true, [new("x", 0, null)]))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Activities_are_private_to_their_workspace_and_can_be_edited_and_deleted()
    {
        var (s, _) = await StartAsync();
        var other = await Workspaces.SignupAsync(factory);
        var task = await CreateAsync(s, Task("Private"));

        (await other.Client.GetAsync($"/api/v1/activities/{task.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var edited = await SendAsync<ActivityDto>(await s.Client.PutAsJsonAsync($"/api/v1/activities/{task.Id}", Task("Renamed", DateTime.UtcNow.AddDays(1))));
        edited.Subject.ShouldBe("Renamed");
        (await s.Client.PutAsJsonAsync($"/api/v1/activities/{task.Id}", Task("x") with { Type = "Call" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.DeleteAsync($"/api/v1/activities/{task.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await s.Client.GetAsync($"/api/v1/activities/{task.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
