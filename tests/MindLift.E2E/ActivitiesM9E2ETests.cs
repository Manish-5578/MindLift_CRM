using System.Net.Http.Json;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>Quick add, timelines, mentions and meeting invitations (sent through a real mail server).</summary>
[Collection(E2ECollection.Name)]
public sealed class ActivitiesM9E2ETests(E2EHost host)
{
    private async Task<(IPage Page, Workspace Workspace, HttpClient Api)> StartAsync(int width = 1440)
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync(width);
        await E2EHost.LoginAsync(page, workspace);
        return (page, workspace, host.ApiFor(workspace));
    }

    private static ILocator Drawer(IPage page) => page.Locator("[data-testid=form-drawer].mud-drawer--open");

    private static async Task<ContactDto> ContactAsync(HttpClient api, string first = "Pat")
        => (await (await api.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest(first, "Tester", null, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<ContactDto>())!;

    [E2EFact]
    public async Task The_plus_button_adds_a_task_from_any_page()
    {
        var (page, _, api) = await StartAsync();
        await page.GotoAsync("/contacts");

        await page.Locator("[data-testid=quick-add]").ClickAsync();
        await page.Locator("[data-testid=quick-add-task]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=activity-subject]").FillAsync("Quick one");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();
        await Expect(page.GetByText("Activity saved")).ToBeVisibleAsync();

        var list = await (await api.PostAsJsonAsync("/api/v1/activities/query", new ActivityQuery("all", null, null, false, null, null, null, null))).Content.ReadFromJsonAsync<PagedResult<ActivityDto>>();
        Assert.Equal("Quick one", list!.Items.Single().Subject);
    }

    [E2EFact]
    public async Task A_note_can_mention_a_colleague_and_the_mention_shows_on_the_note()
    {
        var (page, _, api) = await StartAsync();
        var contact = await ContactAsync(api);
        await page.GotoAsync($"/contacts/{contact.Id}");

        await page.Locator("[data-testid=add-note]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=activity-subject]").FillAsync("Please review");
        await Drawer(page).Locator("input[data-testid=activity-mentions]").ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Eva Tester" }).First.ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Expect(page.Locator("[data-testid=activity-row]")).ToContainTextAsync("@Eva Tester");
    }

    [E2EFact]
    public async Task A_meeting_with_a_guest_and_invitations_ticked_puts_a_calendar_file_in_the_guest_inbox()
    {
        var (page, _, api) = await StartAsync();
        var contact = await ContactAsync(api);
        var guest = $"guest-{Guid.NewGuid():N}@example.test";
        await page.GotoAsync($"/contacts/{contact.Id}");

        await page.Locator("[data-testid=add-meeting]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=activity-subject]").FillAsync("Demo of the platform");
        await Drawer(page).Locator("input[data-testid=activity-due]").FillAsync(DateTime.Now.AddDays(2).ToString("yyyy-MM-ddTHH:mm"));
        await Drawer(page).Locator("textarea[data-testid=activity-attendees]").FillAsync(guest);
        await Drawer(page).Locator("[data-testid=activity-send-invites]").First.ClickAsync();
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();
        await Expect(page.Locator("[data-testid=activity-row]")).ToContainTextAsync("Demo of the platform");

        var email = await host.WaitForEmailAsync(guest, "Invitation: Demo of the platform");
        Assert.Contains("Demo of the platform", email.Text);
        using var http = new HttpClient { BaseAddress = new Uri(host.MailApi) };
        var message = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync($"/api/v1/message/{email.Id}"));
        var attachment = message.RootElement.GetProperty("Attachments").EnumerateArray().Single();
        Assert.Equal("invite.ics", attachment.GetProperty("FileName").GetString());
        var part = attachment.GetProperty("PartID").GetString();
        var ics = await http.GetStringAsync($"/api/v1/message/{email.Id}/part/{part}");
        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("SUMMARY:Demo of the platform", ics);
        await Shots.SaveAsync(page, "contact-meeting");
    }

    [E2EFact]
    public async Task A_deal_page_timeline_shows_a_stage_move_and_a_new_note_straight_away()
    {
        var (page, _, api) = await StartAsync();
        var pipeline = (await api.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!.First(p => p.IsDefault);
        var deal = await (await api.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Timed deal", null, null, 10, null, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<DealDto>();
        var second = pipeline.Stages.Where(s => s.StageType == "Open").OrderBy(s => s.SortOrder).ElementAt(1);
        await page.GotoAsync($"/deals/{deal!.Id}");
        await Expect(page.Locator("[data-testid=record-timeline]")).ToContainTextAsync("Deal created");

        await page.Locator($"[data-testid=stage-{second.Id}]").ClickAsync();
        await Expect(page.Locator("[data-testid=record-timeline]")).ToContainTextAsync("Moved from");

        await page.Locator("[data-testid=add-note]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=activity-subject]").FillAsync("Spoke to the buyer");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();
        await Expect(page.Locator("[data-testid=record-timeline]")).ToContainTextAsync("Note: Spoke to the buyer");
        await Shots.SaveAsync(page, "deal-timeline");
    }

    [E2EFact]
    public async Task A_lead_page_shows_its_timeline_with_field_changes()
    {
        var (page, _, api) = await StartAsync();
        var lead = await (await api.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Change", "Lead", null, null, "Before Co", "new", "website", null, null, null, null, null))).Content.ReadFromJsonAsync<LeadDto>();
        await api.PutAsJsonAsync($"/api/v1/leads/{lead!.Id}", new SaveLeadRequest("Change", "Lead", null, null, "After Co", "new", "website", null, null, null, null, null));

        await page.GotoAsync($"/leads/{lead.Id}");

        await Expect(page.Locator("[data-testid=record-timeline]")).ToContainTextAsync("Company Name changed");
        await Expect(page.Locator("[data-testid=record-timeline]")).ToContainTextAsync("Before Co → After Co");
    }
}
