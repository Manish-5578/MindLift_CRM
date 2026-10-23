using System.Net.Http.Json;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>
/// The calendar (plan 18.9.7) in a browser set to Dubai time (UTC+4): items sit at the person's own hour, dragging one to another hour moves it,
/// and clicking one opens its card.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class CalendarE2ETests(E2EHost host)
{
    private const string Dubai = "Asia/Dubai";

    private static DateOnly TodayInDubai() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(4));

    private static async Task<ActivityDto> MeetingAsync(HttpClient api, string subject, DateOnly day, int dubaiHour)
    {
        var start = day.ToDateTime(new TimeOnly(dubaiHour, 0), DateTimeKind.Utc).AddHours(-4);
        var response = await api.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest(
            "Meeting", subject, null, null, start, start.AddHours(1), null, null, null, "Room 4", null, null, null, null, null, null, ["guest@example.test"]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ActivityDto>())!;
    }

    private static ILocator Drawer(IPage page) => page.Locator("[data-testid=form-drawer].mud-drawer--open");

    private static string Slot(DateOnly day, int hour) => $"[data-testid=cal-slot-{day:yyyy-MM-dd}-{hour:00}]";

    private async Task<(IPage Page, HttpClient Api)> StartAsync()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync(timezoneId: Dubai);
        await E2EHost.LoginAsync(page, workspace);
        return (page, host.ApiFor(workspace));
    }

    [E2EFact]
    public async Task A_meeting_shows_at_the_local_hour_and_moves_when_dragged_to_another_hour()
    {
        var (page, api) = await StartAsync();
        var day = TodayInDubai();
        var meeting = await MeetingAsync(api, "Site visit", day, 10);

        await page.GotoAsync("/calendar");
        await Expect(page.Locator("[data-testid=cal-zone]")).ToContainTextAsync(Dubai);
        var item = page.Locator($"{Slot(day, 10)} [data-testid=cal-event]", new() { HasText = "Site visit" });
        await Expect(item).ToContainTextAsync("10:00–11:00");
        await Shots.SaveAsync(page, "calendar-week");

        await item.DragToAsync(page.Locator(Slot(day, 14)));

        await Expect(page.Locator($"{Slot(day, 14)} [data-testid=cal-event]", new() { HasText = "Site visit" })).ToContainTextAsync("14:00–15:00");
        var moved = (await api.GetFromJsonAsync<ActivityDto>($"/api/v1/activities/{meeting.Id}"))!;
        moved.DueAtUtc.ShouldBe(day.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
        moved.EndAtUtc.ShouldBe(day.ToDateTime(new TimeOnly(11, 0), DateTimeKind.Utc));
    }

    [E2EFact]
    public async Task Clicking_a_meeting_opens_its_card_with_place_people_and_edit()
    {
        var (page, api) = await StartAsync();
        var day = TodayInDubai();
        await MeetingAsync(api, "Contract review", day, 9);

        await page.GotoAsync("/calendar");
        await page.Locator("[data-testid=cal-event]", new() { HasText = "Contract review" }).ClickAsync();

        await Expect(page.Locator("[data-testid=cal-card-time]")).ToContainTextAsync("09:00–10:00");
        await Expect(page.Locator("[data-testid=cal-card-location]")).ToHaveTextAsync("Room 4");
        await Expect(page.Locator("[data-testid=cal-card-attendees]")).ToContainTextAsync("guest@example.test");
        await page.Locator("[data-testid=cal-card-edit]").ClickAsync();
        await Expect(Drawer(page).Locator("input[data-testid=activity-subject]")).ToHaveValueAsync("Contract review");
    }

    [E2EFact]
    public async Task The_new_meeting_button_opens_the_meeting_form()
    {
        var (page, _) = await StartAsync();

        await page.GotoAsync("/calendar");
        await page.Locator("[data-testid=cal-new-meeting]").ClickAsync();

        await Expect(Drawer(page).Locator("input[data-testid=activity-subject]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task An_admin_can_show_a_teams_calendar()
    {
        var (page, api) = await StartAsync();
        (await api.PostAsJsonAsync("/api/v1/teams", new SaveTeamRequest("Field sales", null, null))).EnsureSuccessStatusCode();

        await page.GotoAsync("/calendar");
        await page.Locator("div[data-testid=cal-who]").ClickAsync();
        await page.GetByText("Team: Field sales").ClickAsync();

        await Expect(page.Locator("[data-testid=cal-week]")).ToBeVisibleAsync();
    }
}
