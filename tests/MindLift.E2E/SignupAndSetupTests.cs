using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>A new person signs up and gets through the setup wizard to a working dashboard, using only the screens.</summary>
[Collection(E2ECollection.Name)]
public sealed class SignupAndSetupTests(E2EHost host)
{
    [E2EFact]
    public async Task A_new_person_can_sign_up_finish_the_wizard_and_reach_the_dashboard()
    {
        var page = await host.NewPageAsync();
        var email = $"signup-{Guid.NewGuid():N}@example.test";

        await page.GotoAsync("/signup");
        await Expect(page.Locator("[data-testid=page-title]")).ToHaveTextAsync("Create your workspace");
        await page.Locator("input[data-testid=signup-company]").FillAsync("Browser Test Co");
        await page.Locator("input[data-testid=signup-first]").FillAsync("Eva");
        await page.Locator("input[data-testid=signup-last]").FillAsync("Tester");
        await page.Locator("input[data-testid=signup-email]").FillAsync(email);
        await page.Locator("input[data-testid=signup-password]").FillAsync("Str0ngPass1");
        await page.GetByText("I agree to the Terms and Privacy Policy").ClickAsync();
        await page.Locator("button[data-testid=primary-action]").ClickAsync();

        // Straight into the wizard.
        await page.WaitForURLAsync("**/onboarding");
        await Expect(page.Locator("[data-testid=step-indicator]")).ToContainTextAsync("Step 1 of 6");

        for (var step = 1; step <= 5; step++)
        {
            await page.Locator("button[data-testid=primary-action]").ClickAsync();
            await Expect(page.Locator("[data-testid=step-indicator]")).ToContainTextAsync($"Step {step + 1} of 6");
        }

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/onboarding"));

        await Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Leads" })).ToBeVisibleAsync();
        await Shots.SaveAsync(page, "dashboard-after-setup");
    }

    [E2EFact]
    public async Task Someone_who_has_not_finished_setup_is_sent_back_to_the_wizard_from_every_page()
    {
        var workspace = await host.NewWorkspaceAsync(finishSetup: false);
        var page = await host.NewPageAsync();

        await E2EHost.LoginAsync(page, workspace);

        foreach (var path in new[] { "/leads", "/settings/company", "/" })
        {
            await page.GotoAsync(path);
            await page.WaitForURLAsync("**/onboarding");
            await Expect(page.Locator("[data-testid=step-indicator]")).ToBeVisibleAsync();
        }
    }

    [E2EFact]
    public async Task Someone_who_has_finished_setup_is_not_sent_to_the_wizard()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();

        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/leads");

        await Expect(page.Locator("[data-testid=entity-list]")).ToBeVisibleAsync();
        Assert.DoesNotContain("/onboarding", page.Url);
    }

    [E2EFact]
    public async Task A_wrong_password_shows_an_error_and_stays_on_the_sign_in_page()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();

        await page.GotoAsync("/login");
        await page.Locator("input[data-testid=login-email]").FillAsync(workspace.Email);
        await page.Locator("input[data-testid=login-password]").FillAsync("Wrong0Password");
        await page.Locator("button[data-testid=primary-action]").ClickAsync();

        await Expect(page.Locator("[data-testid=login-error]")).ToBeVisibleAsync();
        Assert.Contains("/login", page.Url);
    }
}
