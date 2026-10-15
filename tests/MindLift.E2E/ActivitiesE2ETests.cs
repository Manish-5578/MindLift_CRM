using System.Net.Http.Json;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>Activities on record pages, the My Activities page and onboarding checklists.</summary>
[Collection(E2ECollection.Name)]
public sealed class ActivitiesE2ETests(E2EHost host)
{
    private async Task<(IPage Page, HttpClient Api, PipelineDto Pipeline)> StartAsync(int width = 1440)
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync(width);
        await E2EHost.LoginAsync(page, workspace);
        var api = host.ApiFor(workspace);
        var pipelines = (await api.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!;
        return (page, api, pipelines.First(p => p.IsDefault));
    }

    private static ILocator Drawer(IPage page) => page.Locator("[data-testid=form-drawer].mud-drawer--open");

    private static async Task<ContactDto> ContactAsync(HttpClient api, string first = "Pat")
        => (await (await api.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest(first, "Tester", null, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<ContactDto>())!;

    private static async Task<ActivityDto> TaskAsync(HttpClient api, string subject, DateTime? due, Guid? contact = null)
        => (await (await api.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest("Task", subject, null, null, due, null, null, null, null, null, null, null, contact, null, null, null))).Content.ReadFromJsonAsync<ActivityDto>())!;

    [E2EFact]
    public async Task A_task_can_be_added_on_a_contact_page_and_ticked_off()
    {
        var (page, api, _) = await StartAsync();
        var contact = await ContactAsync(api);
        await page.GotoAsync($"/contacts/{contact.Id}");
        await Expect(page.Locator("[data-testid=no-activities]")).ToBeVisibleAsync();

        await page.Locator("[data-testid=add-task]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=activity-subject]").FillAsync("Send the quote");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Expect(page.Locator("[data-testid=activity-row]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=activity-row]")).ToContainTextAsync("Send the quote");

        await page.GetByLabel("Complete Send the quote").CheckAsync();
        await Expect(page.Locator("[data-testid=activity-row] .mud-typography-subtitle2")).ToHaveCSSAsync("text-decoration-line", "line-through");
        await Shots.SaveAsync(page, "contact-with-activities");
    }

    [E2EFact]
    public async Task A_subject_is_required()
    {
        var (page, api, _) = await StartAsync();
        var contact = await ContactAsync(api);
        await page.GotoAsync($"/contacts/{contact.Id}");

        await page.Locator("[data-testid=add-task]").ClickAsync();
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Expect(Drawer(page)).ToContainTextAsync("Enter a subject.");
    }

    [E2EFact]
    public async Task A_note_can_be_added_on_a_lead_page()
    {
        var (page, api, _) = await StartAsync();
        var lead = await (await api.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Note", "Lead", null, null, null, "new", "website", null, null, null, null, null))).Content.ReadFromJsonAsync<LeadDto>();
        await page.GotoAsync($"/leads/{lead!.Id}");

        await page.Locator("[data-testid=add-note]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=activity-subject]").FillAsync("Met at the expo");
        await Drawer(page).Locator("textarea[data-testid=activity-description]").FillAsync("Interested in the premium plan");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Expect(page.Locator("[data-testid=activity-row]")).ToContainTextAsync("Interested in the premium plan");
    }

    [E2EFact]
    public async Task A_logged_call_shows_on_the_contact_page_with_its_direction()
    {
        var (page, api, _) = await StartAsync();
        var contact = await ContactAsync(api);
        await page.GotoAsync($"/contacts/{contact.Id}");

        await page.Locator("[data-testid=add-call]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=activity-subject]").FillAsync("Intro call");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Expect(page.Locator("[data-testid=activity-row]")).ToContainTextAsync("Call · Outbound");
    }

    [E2EFact]
    public async Task My_activities_splits_work_into_tabs_with_counts_and_completing_moves_it()
    {
        var (page, api, _) = await StartAsync();
        await TaskAsync(api, "Late one", DateTime.UtcNow.AddDays(-2));
        await TaskAsync(api, "Soon one", DateTime.UtcNow.AddDays(4));
        await page.GotoAsync("/activities");

        await Expect(page.Locator("[data-testid=tab-overdue]")).ToContainTextAsync("Overdue (1)");
        await Expect(page.Locator("[data-testid=tab-upcoming]")).ToContainTextAsync("Upcoming (1)");

        await page.Locator("[data-testid=tab-overdue]").ClickAsync();
        await Expect(page.Locator("[data-testid=activity-row]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=activity-row]")).ToContainTextAsync("Late one");
        await page.GetByLabel("Complete Late one").CheckAsync();
        await Expect(page.Locator("[data-testid=tab-overdue]")).ToContainTextAsync("Overdue (0)");

        await page.Locator("[data-testid=tab-completed]").ClickAsync();
        await Expect(page.Locator("[data-testid=activity-row]")).ToContainTextAsync("Late one");
        await Shots.SaveAsync(page, "my-activities");
    }

    [E2EFact]
    public async Task My_activities_says_when_there_is_nothing_to_do()
    {
        var (page, _, _) = await StartAsync();
        await page.GotoAsync("/activities");

        await Expect(page.Locator("[data-testid=empty-state]")).ToContainTextAsync("Nothing due today");
    }

    [E2EFact]
    public async Task A_checklist_can_be_created_edited_and_deleted_in_settings()
    {
        var (page, _, _) = await StartAsync();
        await page.GotoAsync("/settings/checklists");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=checklist-name]").FillAsync("Welcome");
        await Drawer(page).Locator("input[data-testid=item-subject]").First.FillAsync("Send contract");
        await Drawer(page).Locator("[data-testid=add-checklist-item]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=item-subject]").Nth(1).FillAsync("Kick-off call");
        await Drawer(page).Locator("input[data-testid=item-days]").Nth(1).FillAsync("7");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        var card = page.Locator("[data-testid=checklist-Welcome]");
        await Expect(card).ToContainTextAsync("Send contract");
        await Expect(card).ToContainTextAsync("+7 days");
        await Shots.SaveAsync(page, "settings-checklists");

        await card.Locator("[data-testid=edit-checklist]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=checklist-name]").FillAsync("Welcome kit");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();
        await Expect(page.Locator("[data-testid=checklist-Welcome\\ kit]")).ToBeVisibleAsync();

        await page.Locator("[data-testid=delete-checklist]").ClickAsync();
        await page.Locator("[data-testid=confirm-ok]").ClickAsync();
        await Expect(page.GetByText("No checklists yet")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task A_checklist_needs_a_name_and_a_task_subject()
    {
        var (page, _, _) = await StartAsync();
        await page.GotoAsync("/settings/checklists");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Expect(Drawer(page).Locator("[data-testid=checklist-error]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task Winning_a_deal_shows_the_onboarding_tasks_and_their_progress_on_the_deal_page()
    {
        var (page, api, pipeline) = await StartAsync();
        await api.PostAsJsonAsync("/api/v1/settings/checklists", new SaveChecklistTemplateRequest("Welcome", null, true, [new("Send contract", 1, null), new("Kick-off call", 7, null)]));
        var deal = await (await api.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Won deal", null, null, 100, null, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<DealDto>();
        var won = pipeline.Stages.First(s => s.StageType == "Won");
        await page.GotoAsync($"/deals/{deal!.Id}");

        await page.Locator($"[data-testid=stage-{won.Id}]").ClickAsync();
        await page.Locator("[data-testid=confirm-ok]").ClickAsync();

        await Expect(page.Locator("[data-testid=onboarding-count]")).ToHaveTextAsync("0 of 2 done");
        await page.GetByLabel("Complete Send contract").CheckAsync();
        await Expect(page.Locator("[data-testid=onboarding-count]")).ToHaveTextAsync("1 of 2 done");
        await Shots.SaveAsync(page, "deal-onboarding");
    }

    [E2EFact]
    public async Task A_won_deal_with_no_checklist_says_how_to_add_one()
    {
        var (page, api, pipeline) = await StartAsync();
        var deal = await (await api.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Plain win", null, null, 100, null, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<DealDto>();
        await api.PatchAsJsonAsync($"/api/v1/deals/{deal!.Id}/stage", new ChangeDealStageRequest(pipeline.Stages.First(s => s.StageType == "Won").Id, null, null, null, null));

        await page.GotoAsync($"/deals/{deal.Id}");

        await Expect(page.Locator("[data-testid=no-onboarding]")).ToBeVisibleAsync();
    }
}
