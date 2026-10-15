using System.Net.Http.Json;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>Real emails: the app sends through SMTP to a Mailpit server, the tests read the inbox and follow the links, as a person would.</summary>
[Collection(E2ECollection.Name)]
public sealed class EmailE2ETests(E2EHost host)
{
    private const string NewPassword = "Another0Passw0rd";

    [E2EFact]
    public async Task A_forgotten_password_can_be_reset_with_the_emailed_link_and_the_new_password_signs_in()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();

        await page.GotoAsync("/login");
        await page.Locator("[data-testid=forgot-link]").ClickAsync();
        await page.Locator("input[data-testid=forgot-email]").FillAsync(workspace.Email);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.Locator("[data-testid=forgot-sent]")).ToBeVisibleAsync();

        var email = await host.WaitForEmailAsync(workspace.Email, "Reset your MindLift password");
        await page.GotoAsync(email.FirstLink());
        await page.Locator("input[data-testid=reset-password]").FillAsync(NewPassword);
        await page.Locator("input[data-testid=reset-confirm]").FillAsync(NewPassword);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.Locator("[data-testid=reset-done]")).ToBeVisibleAsync();

        await page.Locator("[data-testid=primary-action]").ClickAsync();
        await page.Locator("input[data-testid=login-email]").FillAsync(workspace.Email);
        await page.Locator("input[data-testid=login-password]").FillAsync(NewPassword);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/login"));
        await Shots.SaveAsync(page, "after-password-reset");
    }

    [E2EFact]
    public async Task A_reset_link_works_once_and_the_old_password_stops_working()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await page.GotoAsync("/forgot-password");
        await page.Locator("input[data-testid=forgot-email]").FillAsync(workspace.Email);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        var link = (await host.WaitForEmailAsync(workspace.Email, "Reset your MindLift password")).FirstLink();

        await page.GotoAsync(link);
        await page.Locator("input[data-testid=reset-password]").FillAsync(NewPassword);
        await page.Locator("input[data-testid=reset-confirm]").FillAsync(NewPassword);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.Locator("[data-testid=reset-done]")).ToBeVisibleAsync();

        await page.GotoAsync(link);
        await page.Locator("input[data-testid=reset-password]").FillAsync("Third0Passw0rdX");
        await page.Locator("input[data-testid=reset-confirm]").FillAsync("Third0Passw0rdX");
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.Locator("[data-testid=reset-error]")).ToBeVisibleAsync();

        await page.GotoAsync("/login");
        await page.Locator("input[data-testid=login-email]").FillAsync(workspace.Email);
        await page.Locator("input[data-testid=login-password]").FillAsync(workspace.Password);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.Locator("[data-testid=login-error]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task The_two_new_passwords_must_match_and_a_weak_one_is_refused()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await page.GotoAsync("/forgot-password");
        await page.Locator("input[data-testid=forgot-email]").FillAsync(workspace.Email);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await page.GotoAsync((await host.WaitForEmailAsync(workspace.Email, "Reset your MindLift password")).FirstLink());

        await page.Locator("input[data-testid=reset-password]").FillAsync(NewPassword);
        await page.Locator("input[data-testid=reset-confirm]").FillAsync("Different0Passw0rd");
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.Locator("[data-testid=reset-error]")).ToContainTextAsync("not the same");

        await page.Locator("input[data-testid=reset-password]").FillAsync("weak");
        await page.Locator("input[data-testid=reset-confirm]").FillAsync("weak");
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.Locator("[data-testid=reset-error]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task Asking_for_a_reset_for_an_unknown_email_looks_the_same_and_sends_nothing()
    {
        var page = await host.NewPageAsync();
        var stranger = $"nobody-{Guid.NewGuid():N}@example.test";

        await page.GotoAsync("/forgot-password");
        await page.Locator("input[data-testid=forgot-email]").FillAsync(stranger);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();

        await Expect(page.Locator("[data-testid=forgot-sent]")).ToBeVisibleAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => host.WaitForEmailAsync(stranger, "Reset", seconds: 3));
    }

    [E2EFact]
    public async Task A_garbled_reset_link_says_so_instead_of_changing_anything()
    {
        var page = await host.NewPageAsync();

        await page.GotoAsync("/reset-password");
        await Expect(page.Locator("[data-testid=reset-invalid]")).ToBeVisibleAsync();

        await page.GotoAsync("/reset-password?email=someone%40example.test&token=garbage");
        await page.Locator("input[data-testid=reset-password]").FillAsync(NewPassword);
        await page.Locator("input[data-testid=reset-confirm]").FillAsync(NewPassword);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.Locator("[data-testid=reset-error]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task The_verification_email_confirms_the_address_when_its_link_is_opened()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        var api = host.ApiFor(workspace);
        (await api.PostAsync("/api/v1/auth/send-verification", null)).EnsureSuccessStatusCode();

        var email = await host.WaitForEmailAsync(workspace.Email, "Confirm your MindLift email");
        await page.GotoAsync(email.FirstLink());

        await Expect(page.Locator("[data-testid=verify-ok]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task A_broken_verification_link_shows_an_error()
    {
        var page = await host.NewPageAsync();

        await page.GotoAsync("/verify-email?email=someone%40example.test&token=garbage");

        await Expect(page.Locator("[data-testid=verify-error]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task An_invited_person_follows_the_emailed_link_chooses_a_password_and_lands_signed_in()
    {
        var workspace = await host.NewWorkspaceAsync(company: "Invite Co");
        var api = host.ApiFor(workspace);
        var roles = (await api.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles"))!;
        var guest = $"guest-{Guid.NewGuid():N}@example.test";
        (await api.PostAsJsonAsync("/api/v1/invitations", new CreateInvitationRequest(guest, "Gus", "Guest", roles.First(r => !r.IsAdmin).Id, null))).EnsureSuccessStatusCode();
        var page = await host.NewPageAsync();

        var email = await host.WaitForEmailAsync(guest, "invited");
        await page.GotoAsync(email.FirstLink());
        await Expect(page.Locator("[data-testid=invite-summary]")).ToContainTextAsync("Invite Co");
        await page.Locator("input[data-testid=invite-password]").FillAsync(NewPassword);
        await page.Locator("input[data-testid=invite-confirm]").FillAsync(NewPassword);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();

        await page.WaitForURLAsync(url => !url.Contains("accept-invite"));
        await Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Leads" })).ToBeVisibleAsync();
        await Shots.SaveAsync(page, "after-invitation");
    }

    [E2EFact]
    public async Task An_invitation_link_that_was_withdrawn_or_made_up_is_refused()
    {
        var page = await host.NewPageAsync();

        await page.GotoAsync("/accept-invite?token=not-a-real-token");

        await Expect(page.Locator("[data-testid=invite-invalid]")).ToBeVisibleAsync();
    }
}
