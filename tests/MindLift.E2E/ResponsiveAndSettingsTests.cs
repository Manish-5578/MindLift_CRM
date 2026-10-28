using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>Settings screens open and work, and every key page fits phone, tablet and desktop in English and Arabic.</summary>
[Collection(E2ECollection.Name)]
public sealed class ResponsiveAndSettingsTests(E2EHost host)
{
    private static readonly string[] Pages =
    [
        "/", "/leads", "/leads/board", "/leads/import", "/contacts", "/accounts", "/contacts/import", "/deals", "/deals/board", "/deals/import", "/activities", "/settings/checklists", "/settings/booking-pages", "/calendar", "/notifications", "/notifications/preferences",
        "/settings/company", "/settings/users", "/settings/roles", "/settings/teams", "/settings/pipelines",
        "/settings/picklists", "/settings/custom-fields", "/settings/layouts",
        "/settings/currencies", "/settings/assignment-rules", "/settings/web-forms",
    ];

    [E2ETheory]
    [InlineData("en", 1440)]
    [InlineData("en", 768)]
    [InlineData("en", 375)]
    [InlineData("ar", 1440)]
    [InlineData("ar", 375)]
    public async Task Every_key_page_fits_the_screen_without_sideways_scrolling(string language, int width)
    {
        var workspace = await host.NewWorkspaceAsync(language: language);
        var page = await host.NewPageAsync(width, width < 500 ? 812 : 900, language == "ar" ? "ar-AE" : "en-US");
        await E2EHost.LoginAsync(page, workspace);
        var wide = new List<string>();

        foreach (var path in Pages)
        {
            await page.GotoAsync(path);
            await Expect(page.Locator("[data-testid=page-title]").First).ToBeVisibleAsync();
            await page.WaitForTimeoutAsync(400);
            var overflow = await page.EvaluateAsync<int>("document.documentElement.scrollWidth - document.documentElement.clientWidth");
            if (overflow > 1)
            {
                wide.Add($"{path} (+{overflow}px)");
            }

            await Shots.SaveAsync(page, $"{(path == "/" ? "home" : path.Trim('/').Replace('/', '-'))}-{language}");
        }

        Assert.True(wide.Count == 0, "Pages that scroll sideways: " + string.Join(", ", wide));
        if (language == "ar")
        {
            Assert.Equal("rtl", await page.EvaluateAsync<string>("document.querySelector(\"[dir=rtl]\") ? \"rtl\" : \"ltr\""));
        }
    }

    [E2EFact]
    public async Task The_brand_colour_can_be_saved_and_the_swatch_shows_it()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/settings/company");

        await page.Locator("input[data-testid=brand-colour-input]").FillAsync("#0A7B3E");
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Expect(page.GetByText("saved", new PageGetByTextOptions { Exact = false }).First).ToBeVisibleAsync();

        await page.ReloadAsync();
        await Expect(page.Locator("input[data-testid=brand-colour-input]")).ToHaveValueAsync("#0A7B3E");
    }

    [E2EFact]
    public async Task A_bad_brand_colour_is_refused()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/settings/company");

        await page.Locator("input[data-testid=brand-colour-input]").FillAsync("red");
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await page.ReloadAsync();

        await Expect(page.Locator("input[data-testid=brand-colour-input]")).Not.ToHaveValueAsync("red");
    }

    [E2EFact]
    public async Task The_style_guide_is_available_in_development()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/_styleguide");
        await Expect(page.Locator("main, .mud-main-content").First).ToContainTextAsync("Style");
    }
}
