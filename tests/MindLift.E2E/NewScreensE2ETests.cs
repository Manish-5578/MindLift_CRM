using System.Net.Http.Json;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>The newest screens in a real browser: billing, email sending, your data, tickets, campaigns, calendar connections, the dashboard, and the platform console's Billing, Data and Support views.</summary>
[Collection(E2ECollection.Name)]
public sealed class NewScreensE2ETests(E2EHost host)
{
    private async Task<(IPage Page, Workspace Workspace, HttpClient Api)> CompanyAsync(string? name = null, int width = 1440)
    {
        var workspace = await host.NewWorkspaceAsync(company: name);
        var page = await host.NewPageAsync(width);
        await E2EHost.LoginAsync(page, workspace);
        return (page, workspace, host.ApiFor(workspace));
    }

    private async Task OpenCompanyAsync(IPage page, string name)
    {
        await page.GotoAsync("/platform/companies");
        await page.Locator("input[data-testid=company-filter]").FillAsync(name);
        var card = page.Locator("[data-testid=company-card]").Filter(new LocatorFilterOptions { HasText = name });
        await Expect(card).ToHaveCountAsync(1);
        await card.ClickAsync();
        await Expect(page.Locator("[data-testid=company-name]")).ToHaveTextAsync(name);
    }

    [E2EFact]
    public async Task A_company_chooses_a_plan_on_the_billing_page_and_the_owner_marks_it_paid()
    {
        var name = $"Bill Co {Guid.NewGuid():N}"[..18];
        var (page, workspace, _) = await CompanyAsync(name);
        await page.GotoAsync("/settings/billing");
        await Expect(page.Locator("[data-testid=billing-plans]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=billing-trial]")).ToBeVisibleAsync();
        await Shots.SaveAsync(page, "billing-company");

        await page.Locator("[data-testid=interval-yearly]").ClickAsync();
        await page.Locator("[data-testid=choose-starter]").ClickAsync();
        await Expect(page.Locator("[data-testid=billing-pending]")).ToContainTextAsync("Starter");
        await Expect(page.Locator("[data-testid=billing-invoice]")).ToHaveCountAsync(1);

        var owner = await host.NewPlatformOwnerAsync();
        var ownerPage = await host.NewPageAsync();
        await E2EHost.LoginAsync(ownerPage, owner);
        await OpenCompanyAsync(ownerPage, name);
        await ownerPage.Locator("[data-testid=tab-billing]").ClickAsync();
        await Expect(ownerPage.Locator("[data-testid=bill-pending]")).ToBeVisibleAsync();
        await Shots.SaveAsync(ownerPage, "billing-console-waiting");
        await ownerPage.Locator("[data-testid=bill-mark-paid]").ClickAsync();
        await Expect(ownerPage.Locator("[data-testid=bill-status]")).ToContainTextAsync("Active");

        await page.ReloadAsync();
        await Expect(page.Locator("[data-testid=billing-plan]")).ToContainTextAsync("Starter");
        await Expect(page.Locator("[data-testid=billing-trial]")).ToHaveCountAsync(0);
        await Shots.SaveAsync(page, "billing-company-active");
    }

    [E2EFact]
    public async Task A_company_adds_a_sending_domain_and_sees_what_to_put_in_its_dns()
    {
        var (page, _, _) = await CompanyAsync();
        await page.GotoAsync("/settings/email-sending");
        await page.Locator("input[data-testid=mail-domain-input]").FillAsync("acme-test.com");
        await page.Locator("button[data-testid=mail-domain-add]").ClickAsync();
        var domain = page.Locator("[data-testid=mail-domain]");
        await Expect(domain).ToContainTextAsync("acme-test.com");
        await Expect(domain).ToContainTextAsync("mindlift-verification=");
        await Expect(domain).ToContainTextAsync("v=DMARC1");
        await Shots.SaveAsync(page, "email-sending");
    }

    [E2EFact]
    public async Task A_company_prepares_a_data_export_and_downloads_it()
    {
        var (page, _, _) = await CompanyAsync();
        await page.GotoAsync("/settings/data");
        await page.Locator("button[data-testid=data-export-start]").ClickAsync();
        var row = page.Locator("[data-testid=data-export-row]");
        await Expect(row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Download" })).ToBeVisibleAsync();
        await Shots.SaveAsync(page, "data-export");
        var download = await page.RunAndWaitForDownloadAsync(() => row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Download" }).ClickAsync());
        Assert.EndsWith(".zip", download.SuggestedFilename);
    }

    [E2EFact]
    public async Task Someone_opens_a_ticket_replies_to_it_and_resolves_it()
    {
        var (page, _, _) = await CompanyAsync();
        await page.GotoAsync("/tickets");
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await page.Locator("input[data-testid=ticket-subject]").FillAsync("Printer is on fire");
        await page.Locator("textarea[data-testid=ticket-description]").FillAsync("Smoke everywhere");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Open ticket" }).ClickAsync();
        await Expect(page.Locator("[data-testid=ticket-title]")).ToHaveTextAsync("Printer is on fire");
        await page.Locator("textarea[data-testid=ticket-reply]").FillAsync("We are on our way.");
        await page.Locator("button[data-testid=ticket-send]").ClickAsync();
        await Expect(page.Locator("[data-testid=ticket-comment]")).ToContainTextAsync("We are on our way.");
        await Shots.SaveAsync(page, "ticket-detail");

        await page.Locator("[data-testid=ticket-status]").Last.ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "resolved" }).ClickAsync();
        await page.GotoAsync("/tickets");
        await page.Locator("[data-testid=tf-resolved]").ClickAsync();
        await Expect(page.Locator("[data-testid=ticket-row]")).ToContainTextAsync("Printer is on fire");
        await Shots.SaveAsync(page, "tickets-list");
    }

    [E2EFact]
    public async Task A_campaign_is_written_sent_and_its_results_appear()
    {
        var (page, _, api) = await CompanyAsync();
        for (var i = 0; i < 3; i++)
        {
            (await api.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest($"Reader{i}", "Test", $"reader{i}-{Guid.NewGuid():N}@example.test", null, "Acme", null, null, null, null, null, null, null))).EnsureSuccessStatusCode();
        }

        await page.GotoAsync("/campaigns/new");
        await page.Locator("input[data-testid=campaign-name]").FillAsync("Autumn offer");
        await page.Locator("input[data-testid=campaign-subject]").FillAsync("Hello {{first_name}}");
        await page.Locator("textarea[data-testid=campaign-body]").FillAsync("Hi {{first_name}},\nSee https://example.com/offer");
        await Expect(page.Locator("[data-testid=campaign-preview]")).ToContainTextAsync("3");
        await Shots.SaveAsync(page, "campaign-editor");
        await page.Locator("button[data-testid=campaign-save]").ClickAsync();
        await Expect(page.Locator("[data-testid=campaign-status]")).ToContainTextAsync("draft");

        await page.Locator("button[data-testid=campaign-send]").ClickAsync();
        await Expect(page.Locator("[data-testid=campaign-results]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=campaign-status]")).ToContainTextAsync("sent", new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });
        await Shots.SaveAsync(page, "campaign-results");

        await page.GotoAsync("/campaigns");
        await Expect(page.Locator("[data-testid=campaign-row]")).ToContainTextAsync("Autumn offer");
    }

    [E2EFact]
    public async Task The_calendar_connections_page_shows_the_services_and_makes_a_feed_address()
    {
        var (page, _, _) = await CompanyAsync();
        await page.GotoAsync("/calendar");
        await page.Locator("[data-testid=cal-connect]").ClickAsync();
        await Expect(page.Locator("[data-testid=provider-google]")).ToContainTextAsync("Not set up");
        await page.Locator("[data-testid=feed-new]").ClickAsync();
        await Expect(page.Locator("[data-testid=feed-url]")).ToBeVisibleAsync();
        await Shots.SaveAsync(page, "calendar-connections");
    }

    [E2EFact]
    public async Task The_dashboard_draws_its_charts()
    {
        var (page, _, api) = await CompanyAsync();
        for (var i = 0; i < 5; i++)
        {
            (await api.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest($"Chart{i}", "Lead", null, null, null, null, i % 2 == 0 ? "web" : "referral", null, null, null, null, null))).EnsureSuccessStatusCode();
        }

        await page.GotoAsync("/");
        await Expect(page.Locator("[data-testid=kpi-strip]")).ToBeVisibleAsync();
        await page.GetByText("Company View").ClickAsync();
        await Expect(page.Locator("[data-testid=sales-trend]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=leads-by-source] [data-testid=donut-chart]")).ToBeVisibleAsync();
        await Task.Delay(1200);
        await Shots.SaveAsync(page, "dashboard-charts");
    }

    [E2EFact]
    public async Task The_owner_opens_a_read_only_support_view_and_exports_and_the_console_speaks_arabic()
    {
        var name = $"Support Co {Guid.NewGuid():N}"[..20];
        var (companyPage, _, _) = await CompanyAsync(name);
        var owner = await host.NewPlatformOwnerAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, owner);
        await OpenCompanyAsync(page, name);

        // Data tab: an export, and the removal button stays off until the address is typed.
        await page.Locator("[data-testid=tab-data]").ClickAsync();
        await page.Locator("[data-testid=pc-export]").ClickAsync();
        await Expect(page.Locator("[data-testid=pc-danger]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=purge-button]")).ToBeDisabledAsync();
        await Shots.SaveAsync(page, "console-data");

        // Support view: a reason, then a new tab with a banner, read-only.
        await page.Locator("[data-testid=support-open]").ClickAsync();
        await page.Locator("textarea[data-testid=support-reason]").FillAsync("Customer cannot see their leads");
        var popup = await page.Context.RunAndWaitForPageAsync(() => page.Locator("button[data-testid=support-start-button]").ClickAsync());
        await Expect(popup.Locator("[data-testid=support-banner]")).ToBeVisibleAsync();
        await Expect(popup.Locator("[data-testid=support-banner]")).ToContainTextAsync("Read-only");
        await Shots.SaveAsync(popup, "support-view");
        await popup.Locator("[data-testid=support-end]").ClickAsync();

        // Arabic, right to left.
        await page.GotoAsync("/platform");
        await Expect(page.Locator("[data-testid=kpi-companies]")).ToBeVisibleAsync();
        await page.Locator("[data-testid=platform-language]").ClickAsync();
        await Expect(page.Locator("[data-testid=platform-console]")).ToHaveAttributeAsync("dir", "rtl");
        await Expect(page.Locator("aside.pc-rail")).ToContainTextAsync("الشركات");
        await Task.Delay(800);
        await Shots.SaveAsync(page, "console-arabic");
        await page.GotoAsync("/platform/companies");
        await Expect(page.Locator("[data-testid=company-card]").First).ToBeVisibleAsync();
        await Shots.SaveAsync(page, "console-arabic-companies");
        await companyPage.CloseAsync();
    }
}
