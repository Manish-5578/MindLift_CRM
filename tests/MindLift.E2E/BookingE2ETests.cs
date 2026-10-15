using Shouldly;
using System.Net.Http.Json;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>Booking pages (plan 7.9): a customer books without signing in, then cancels from the emailed link.</summary>
[Collection(E2ECollection.Name)]
public sealed class BookingE2ETests(E2EHost host)
{
    private static AvailabilityDto WeekdayHours() => new("UTC",
        new Dictionary<string, IReadOnlyList<TimeRangeDto>>
        {
            ["mon"] = [new("09:00", "17:00")], ["tue"] = [new("09:00", "17:00")], ["wed"] = [new("09:00", "17:00")],
            ["thu"] = [new("09:00", "17:00")], ["fri"] = [new("09:00", "17:00")], ["sat"] = [], ["sun"] = [],
        },
        []);

    private static async Task<(BookingPageDto Page, string Slug)> OwnPageAsync(HttpClient api, string slug)
    {
        var me = (await api.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;
        var response = await api.PostAsJsonAsync("/api/v1/settings/booking-pages", new SaveBookingPageRequest(slug, "Intro call", "Thirty minutes about your needs",
            30, 0, 0, 30, 1, 30, "SingleUser", [me.UserId], WeekdayHours(), [new("company", "Company", false, "text")], "Video", "https://meet.example.test/x",
            [60, 15], true));
        response.EnsureSuccessStatusCode();
        return ((await response.Content.ReadFromJsonAsync<BookingPageDto>())!, slug);
    }

    private static string ManageLink(InboxMessage message)
        => System.Text.RegularExpressions.Regex.Match(message.Text, @"https?://\S*/booking/manage/\S+").Value;

    [E2EFact]
    public async Task A_customer_books_a_free_time_without_signing_in_and_sees_the_confirmation()
    {
        var owner = await host.NewWorkspaceAsync(company: "Booking Co");
        var api = host.ApiFor(owner);
        var (page, _) = await OwnPageAsync(api, $"intro-{Guid.NewGuid():N}"[..30]);
        var customer = await host.NewPageAsync();
        var email = $"buyer-{Guid.NewGuid():N}@example.test";

        await customer.GotoAsync(page.PublicPath);
        await Expect(customer.Locator("[data-testid=book-title]")).ToHaveTextAsync("Intro call");
        await customer.Locator("[data-testid^=day-]").First.ClickAsync();
        await customer.Locator("[data-testid^=slot-]").First.ClickAsync();
        await customer.Locator("input[data-testid=book-name]").FillAsync("Bea Buyer");
        await customer.Locator("input[data-testid=book-email]").FillAsync(email);
        await customer.Locator("button[data-testid=primary-action]").ClickAsync();

        await Expect(customer.Locator("[data-testid=book-confirmed]")).ToBeVisibleAsync();
        await Shots.SaveAsync(customer, "booking-confirmed");
        var invitation = await host.WaitForEmailAsync(email, "Intro call");
        invitation.Text.ShouldContain("/booking/manage/");

        var meeting = await api.PostAsJsonAsync("/api/v1/activities/query", new ActivityQuery("all", null, null, false, null, null, null, null, 1, 50));
        var list = (await meeting.Content.ReadFromJsonAsync<PagedResult<ActivityDto>>())!;
        Assert.Contains(list.Items, a => a.Subject == "Intro call: Bea Buyer");
    }

    [E2EFact]
    public async Task The_emailed_manage_link_shows_the_booking_and_cancels_it()
    {
        var owner = await host.NewWorkspaceAsync(company: "Cancel Co");
        var api = host.ApiFor(owner);
        var (page, _) = await OwnPageAsync(api, $"cancel-{Guid.NewGuid():N}"[..30]);
        var customer = await host.NewPageAsync();
        var email = $"quitter-{Guid.NewGuid():N}@example.test";
        await customer.GotoAsync(page.PublicPath);
        await customer.Locator("[data-testid^=day-]").First.ClickAsync();
        await customer.Locator("[data-testid^=slot-]").First.ClickAsync();
        await customer.Locator("input[data-testid=book-name]").FillAsync("Quin Quitter");
        await customer.Locator("input[data-testid=book-email]").FillAsync(email);
        await customer.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(customer.Locator("[data-testid=book-confirmed]")).ToBeVisibleAsync();

        var invitation = await host.WaitForEmailAsync(email, "Intro call");
        await customer.GotoAsync(ManageLink(invitation));
        await Expect(customer.Locator("[data-testid=manage-card]")).ToBeVisibleAsync();
        await customer.Locator("input[data-testid=manage-reason]").FillAsync("Plans changed");
        await customer.Locator("[data-testid=manage-cancel]").ClickAsync();

        await Expect(customer.Locator("[data-testid=manage-done]")).ToBeVisibleAsync();
        await host.WaitForEmailAsync(email, "Booking cancelled");
    }

    [E2EFact]
    public async Task An_unknown_booking_page_says_it_is_not_available()
    {
        var customer = await host.NewPageAsync();

        await customer.GotoAsync("/book/no-such-company/no-such-page");

        await Expect(customer.Locator("[data-testid=book-missing]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task A_booking_page_can_be_created_from_settings_and_its_link_is_shown()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/settings/booking-pages");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        var slug = $"settings-{Guid.NewGuid():N}"[..24];
        await page.Locator("input[data-testid=bp-name]").FillAsync("Demo call");
        await page.Locator("input[data-testid=bp-slug]").FillAsync(slug);
        await page.Locator("input[data-testid=bp-duration]").FillAsync("45");
        await page.Locator("[data-testid=form-drawer].mud-drawer--open").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Expect(page.Locator("[data-testid=booking-link]").First).ToContainTextAsync(slug);
        await Shots.SaveAsync(page, "settings-booking-pages");
    }
}
