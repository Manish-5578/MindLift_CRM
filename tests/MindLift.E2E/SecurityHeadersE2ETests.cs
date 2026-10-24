using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>The Content-Security-Policy is enforced on the real apps: the staff app, the platform console and the portal still work and the browser reports no violation.</summary>
[Collection(E2ECollection.Name)]
public sealed class SecurityHeadersE2ETests(E2EHost host)
{
    private static List<string> Watch(IPage page)
    {
        var problems = new List<string>();
        page.Console += (_, m) => { if (m.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase) || m.Text.Contains("Content-Security-Policy", StringComparison.OrdinalIgnoreCase)) problems.Add(m.Text); };
        page.PageError += (_, e) => problems.Add(e);
        return problems;
    }

    [E2EFact]
    public async Task The_staff_app_and_the_platform_console_run_under_an_enforced_policy()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        var problems = Watch(page);
        var response = await page.GotoAsync("/");
        Assert.Contains("script-src 'self'", response!.Headers["content-security-policy"]);
        Assert.DoesNotContain("'unsafe-inline'", response.Headers["content-security-policy"].Split("style-src")[0]);

        await page.WaitForTimeoutAsync(6000);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        await E2EHost.LoginAsync(page, workspace);
        foreach (var path in new[] { "/", "/leads", "/calendar", "/settings/document-types", "/tickets" })
        {
            await page.GotoAsync(path);
            await Expect(page.Locator("main, .mud-main-content").First).ToBeVisibleAsync();
        }

        var owner = await host.NewPlatformOwnerAsync();
        var ownerPage = await host.NewPageAsync();
        var ownerProblems = Watch(ownerPage);
        await E2EHost.LoginAsync(ownerPage, owner);
        await ownerPage.GotoAsync("/platform");
        await Expect(ownerPage.Locator("[data-testid=kpi-companies]")).ToBeVisibleAsync();
        await ownerPage.GotoAsync("/platform/companies");
        await Expect(ownerPage.Locator("[data-testid=company-card]").First).ToBeVisibleAsync();

        Assert.Empty(problems);
        Assert.Empty(ownerProblems);
    }

    // The development server does not serve the portal's own files (they exist only in a published build), so the page itself is not exercised here.
    [E2EFact]
    public async Task The_customer_portal_page_carries_the_policy()
    {
        var page = await host.NewPageAsync();
        var response = await page.GotoAsync("/portal/some-company/login");
        var policy = response!.Headers["content-security-policy"];
        Assert.Contains("script-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
    }
}
