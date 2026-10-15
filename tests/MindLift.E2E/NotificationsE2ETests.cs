using System.Net.Http.Json;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>Two people in one workspace: what one does shows up for the other, live, in the bell, the toast and the notifications page.</summary>
[Collection(E2ECollection.Name)]
public sealed class NotificationsE2ETests(E2EHost host)
{
    private sealed record Colleague(Workspace Login, Guid UserId);

    /// <summary>Invites a second person through the real invitation email and accepts it, so they can sign in.</summary>
    private async Task<Colleague> InviteAsync(Workspace owner)
    {
        var api = host.ApiFor(owner);
        var roles = (await api.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles"))!;
        var email = $"mate-{Guid.NewGuid():N}@example.test";
        (await api.PostAsJsonAsync("/api/v1/invitations", new CreateInvitationRequest(email, "Mia", "Mate", roles.First(r => r.Name == "Staff").Id, null))).EnsureSuccessStatusCode();
        var link = (await host.WaitForEmailAsync(email, "invited")).FirstLink();
        var token = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
        using var http = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
        http.DefaultRequestHeaders.Add("X-Client", "mobile");
        var response = await http.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInvitationRequest(token, "Str0ngPass1"));
        response.EnsureSuccessStatusCode();
        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        using var me = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
        me.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var user = (await me.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;
        return new Colleague(new Workspace(email, "Str0ngPass1", tokens.AccessToken), user.UserId);
    }

    private static async Task<LeadDto> LeadForAsync(HttpClient api, string first, Guid owner)
        => (await (await api.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest(first, "Lead", null, null, "Acme", "new", "website", null, null, null, owner, null))).Content.ReadFromJsonAsync<LeadDto>())!;

    [E2EFact]
    public async Task A_lead_given_to_a_colleague_shows_up_for_them_live_as_a_toast_and_on_the_bell()
    {
        var owner = await host.NewWorkspaceAsync();
        var mate = await InviteAsync(owner);
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, mate.Login);
        await page.GotoAsync("/leads");
        await Expect(page.Locator("[data-testid=notification-bell][data-connected=true]")).ToBeVisibleAsync();

        var lead = await LeadForAsync(host.ApiFor(owner), "Handover", mate.UserId);

        await Expect(page.GetByText("Lead assigned to you: Handover Lead")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5_000 });
        await Expect(page.Locator("[data-testid=notification-badge]")).ToContainTextAsync("1");

        await page.Locator("[data-testid=notification-bell-button]").First.ClickAsync();
        var item = page.Locator("[data-testid=notification-item]");
        await Expect(item).ToHaveCountAsync(1);
        await Shots.SaveAsync(page, "notification-bell");
        await item.ClickAsync();
        await page.WaitForURLAsync($"**/leads/{lead.Id}");
        await Expect(page.Locator("[data-testid=notification-badge] .mud-badge")).ToBeHiddenAsync();
    }

    [E2EFact]
    public async Task The_badge_counts_what_arrived_while_the_person_was_away_and_mark_all_read_clears_it()
    {
        var owner = await host.NewWorkspaceAsync();
        var mate = await InviteAsync(owner);
        var api = host.ApiFor(owner);
        await LeadForAsync(api, "One", mate.UserId);
        await LeadForAsync(api, "Two", mate.UserId);
        var page = await host.NewPageAsync();

        await E2EHost.LoginAsync(page, mate.Login);
        await page.GotoAsync("/leads");

        await Expect(page.Locator("[data-testid=notification-badge]")).ToContainTextAsync("2");
        await page.Locator("[data-testid=notification-bell-button]").First.ClickAsync();
        await page.Locator("[data-testid=mark-all-read]").ClickAsync();
        await Expect(page.Locator("[data-testid=notification-badge] .mud-badge")).ToBeHiddenAsync();
    }

    [E2EFact]
    public async Task The_bell_says_when_there_is_nothing_new()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/leads");

        await page.Locator("[data-testid=notification-bell-button]").First.ClickAsync();

        await Expect(page.Locator("[data-testid=notifications-empty]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task The_notifications_page_lists_everything_filters_unread_and_opens_the_record()
    {
        var owner = await host.NewWorkspaceAsync();
        var mate = await InviteAsync(owner);
        var api = host.ApiFor(owner);
        var lead = await LeadForAsync(api, "Listed", mate.UserId);
        await LeadForAsync(api, "Also", mate.UserId);
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, mate.Login);

        await page.GotoAsync("/notifications");
        await Expect(page.Locator("[data-testid=notification-row]")).ToHaveCountAsync(2);
        await Shots.SaveAsync(page, "notifications-page");

        await page.Locator("[data-testid=notification-row]").Filter(new LocatorFilterOptions { HasText = "Listed Lead" }).ClickAsync();
        await page.WaitForURLAsync($"**/leads/{lead.Id}");
        await page.GotoAsync("/notifications");
        await page.Locator("[data-testid=tab-unread]").ClickAsync();
        await Expect(page.Locator("[data-testid=notification-row]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=notification-row]")).ToContainTextAsync("Also Lead");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.GetByText("You are all caught up")).ToBeVisibleAsync();
        await page.Locator("[data-testid=tab-all]").ClickAsync();
        await Expect(page.Locator("[data-testid=notification-row]")).ToHaveCountAsync(2);
    }

    [E2EFact]
    public async Task Notification_preferences_can_be_changed_saved_and_are_kept_after_a_reload()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/notifications/preferences");
        await Expect(page.Locator("[data-testid=preference-row]")).ToHaveCountAsync(NotificationTypes.All.Count);

        await page.GetByLabel("A lead is assigned to me, by email").UncheckAsync();
        await page.GetByLabel("A deal is won, by email").CheckAsync();
        await page.Locator("[data-testid=digest-switch]").First.ClickAsync();
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.GetByText("Preferences saved")).ToBeVisibleAsync();
        await Shots.SaveAsync(page, "notification-preferences");

        await page.ReloadAsync();
        await Expect(page.GetByLabel("A lead is assigned to me, by email")).Not.ToBeCheckedAsync();
        await Expect(page.GetByLabel("A deal is won, by email")).ToBeCheckedAsync();
        await Expect(page.GetByLabel("A lead is assigned to me, in MindLift")).ToBeCheckedAsync();
    }

    [E2EFact]
    public async Task An_assignment_email_with_a_working_link_reaches_the_colleague_through_the_mail_server()
    {
        var owner = await host.NewWorkspaceAsync();
        var mate = await InviteAsync(owner);
        var lead = await LeadForAsync(host.ApiFor(owner), "Mailed", mate.UserId);

        var email = await host.WaitForEmailAsync(mate.Login.Email, "Lead assigned to you: Mailed Lead");

        Assert.Contains($"/leads/{lead.Id}", email.FirstLink());
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, mate.Login);
        await page.GotoAsync(email.FirstLink());
        await Expect(page.Locator("[data-testid=page-title]")).ToContainTextAsync("Mailed Lead");
    }
}
