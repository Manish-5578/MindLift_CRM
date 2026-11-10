using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>The profile page: language, theme and changing your own password, in a real browser.</summary>
[Collection(E2ECollection.Name)]
public sealed class ProfileE2ETests(E2EHost host)
{
    private async Task<(IPage Page, Workspace Workspace)> StartAsync()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync(1440);
        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/profile");
        await Expect(page.Locator("[data-testid=profile-preferences]")).ToBeVisibleAsync();
        return (page, workspace);
    }

    [E2EFact]
    public async Task Language_and_theme_are_changed_from_the_profile_page()
    {
        var (page, _) = await StartAsync();
        var html = page.Locator("html");

        await page.Locator("[data-testid=lang-ar]").ClickAsync();
        await Expect(html).ToHaveAttributeAsync("dir", "rtl");
        await page.Locator("[data-testid=lang-en]").ClickAsync();
        await Expect(html).ToHaveAttributeAsync("dir", "ltr");

        await page.Locator("[data-testid=theme-dark]").ClickAsync();
        await Expect(html).ToHaveAttributeAsync("data-theme", "dark");
        await page.Locator("[data-testid=theme-light]").ClickAsync();
        await Expect(html).ToHaveAttributeAsync("data-theme", "light");
    }

    [E2EFact]
    public async Task The_notification_preferences_button_opens_the_preferences_page()
    {
        var (page, _) = await StartAsync();
        await page.Locator("[data-testid=profile-notification-prefs]").ClickAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/notifications/preferences$"));
    }

    [E2EFact]
    public async Task A_password_change_needs_the_right_current_password_and_then_the_new_one_signs_you_in()
    {
        var (page, workspace) = await StartAsync();
        const string newPassword = "Fresh-Pass-2026x";
        var card = page.Locator("[data-testid=profile-password-card]");

        await card.Locator("input[data-testid=current-password]").FillAsync(workspace.Password);
        await card.Locator("input[data-testid=profile-new-password]").FillAsync(newPassword);
        await card.Locator("input[data-testid=profile-confirm-password]").FillAsync("something-else");
        await page.Locator("[data-testid=change-password]").ClickAsync();
        await Expect(page.Locator("[data-testid=password-error]")).ToContainTextAsync("do not match");

        await card.Locator("input[data-testid=profile-confirm-password]").FillAsync(newPassword);
        await card.Locator("input[data-testid=current-password]").FillAsync("Wrong-Current-1x");
        await page.Locator("[data-testid=change-password]").ClickAsync();
        await Expect(page.Locator("[data-testid=password-error]")).ToBeVisibleAsync();

        await card.Locator("input[data-testid=current-password]").FillAsync(workspace.Password);
        await page.Locator("[data-testid=change-password]").ClickAsync();
        await Expect(page.GetByText("Your password was changed.")).ToBeVisibleAsync();

        var fresh = await host.NewPageAsync(1440);
        await E2EHost.LoginAsync(fresh, workspace with { Password = newPassword });
        await Expect(fresh).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }
}
