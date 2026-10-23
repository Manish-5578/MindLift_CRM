using System.Net.Http.Json;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>
/// The platform console end to end, in a real browser against the real app: the owner signs in, sees every company with its numbers,
/// chooses which modules a company has, and that company's own workspace changes straight away.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class PlatformConsoleE2ETests(E2EHost host)
{
    private async Task<IPage> OwnerPageAsync(int width = 1440)
    {
        var owner = await host.NewPlatformOwnerAsync();
        var page = await host.NewPageAsync(width);
        await E2EHost.LoginAsync(page, owner);
        return page;
    }

    private static async Task OpenCompanyAsync(IPage page, string name)
    {
        await page.GotoAsync("/platform/companies");
        await page.Locator("input[data-testid=company-filter]").FillAsync(name);
        var card = page.Locator("[data-testid=company-card]").Filter(new LocatorFilterOptions { HasText = name });
        await Expect(card).ToHaveCountAsync(1);
        await card.ClickAsync();
        await Expect(page.Locator("[data-testid=company-name]")).ToHaveTextAsync(name);
    }

    [E2EFact]
    public async Task The_owner_signs_in_lands_in_the_console_and_sees_numbers_for_every_company()
    {
        var name = $"Overview Co {Guid.NewGuid():N}"[..20];
        await host.NewWorkspaceAsync(company: name);
        var page = await OwnerPageAsync();

        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/platform$"));
        await Expect(page.Locator("[data-testid=platform-console]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=kpi-companies]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=area-chart]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=recent-companies]")).ToContainTextAsync(name);
        await Task.Delay(1800); // the numbers count up
        await Shots.SaveAsync(page, "platform-overview");

        await page.Locator("[data-testid=platform-theme]").ClickAsync();
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
        await Shots.SaveAsync(page, "platform-overview-light");
    }

    [E2EFact]
    public async Task The_console_is_usable_on_a_phone()
    {
        await host.NewWorkspaceAsync();
        var page = await OwnerPageAsync(390);
        await Expect(page.Locator("[data-testid=kpi-companies]")).ToBeVisibleAsync();
        var overflow = await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.False(overflow, "The overview scrolls sideways on a phone.");
        await Shots.SaveAsync(page, "platform-overview-phone");

        await page.GotoAsync("/platform/companies");
        await Expect(page.Locator("[data-testid=company-card]").First).ToBeVisibleAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"), "The company list scrolls sideways on a phone.");
        await Shots.SaveAsync(page, "platform-companies-phone");
    }

    [E2EFact]
    public async Task A_person_who_runs_a_company_cannot_use_the_console()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/platform");
        await Expect(page.Locator("[data-testid=platform-denied]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=kpi-companies]")).ToHaveCountAsync(0);
    }

    [E2EFact]
    public async Task The_owner_finds_a_company_and_chooses_its_modules_and_the_company_sees_it_at_once()
    {
        var name = $"Modules Co {Guid.NewGuid():N}"[..20];
        var workspace = await host.NewWorkspaceAsync(company: name);
        var customerApi = host.ApiFor(workspace);
        var customer = await host.NewPageAsync();
        await E2EHost.LoginAsync(customer, workspace);
        await Expect(customer.Locator("a.mud-nav-link[href$='documents']")).ToBeVisibleAsync();

        var page = await OwnerPageAsync();
        await page.Locator("input[data-testid=platform-search]").FillAsync(name);
        await Expect(page.Locator("[data-testid=platform-search-results] .pc-search__item")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=platform-search-results]")).ToContainTextAsync(name);
        await OpenCompanyAsync(page, name);
        await Expect(page.Locator("[data-testid=company-name]")).ToHaveTextAsync(name);

        await page.Locator("[data-testid=tab-modules]").ClickAsync();
        await Expect(page.Locator("[data-testid=save-bar]")).ToHaveCountAsync(0);
        await page.Locator("[data-testid=module-Documents]").ClickAsync();
        await page.Locator("[data-testid=module-Automation]").ClickAsync();
        await Expect(page.Locator("[data-testid=save-bar]")).ToContainTextAsync("2 unsaved changes");
        await Expect(page.Locator("[data-testid=save-bar]")).ToContainTextAsync("Documents");
        await Shots.SaveAsync(page, "platform-company-modules");

        await page.Locator("[data-testid=save-modules]").ClickAsync();
        await Expect(page.Locator("[data-testid=save-bar]")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid=module-Documents]")).ToHaveAttributeAsync("aria-pressed", "false");

        // The company's own workspace: the module is gone from the menu and the API refuses it.
        await customer.ReloadAsync();
        await Expect(customer.Locator("a.mud-nav-link[href$='documents']")).ToHaveCountAsync(0);
        var modules = (await customerApi.GetFromJsonAsync<List<ModuleSettingDto>>("/api/v1/settings/modules"))!;
        Assert.True(modules.Single(m => m.Module == "Documents").PlatformLocked);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await customerApi.GetAsync("/api/v1/automation/rules")).StatusCode);

        // And the history shows who did it.
        await page.Locator("[data-testid=tab-history]").ClickAsync();
        await Expect(page.Locator("[data-testid=history]")).ToContainTextAsync("Priya Owner");

        // Switching it back on restores it.
        await page.Locator("[data-testid=tab-modules]").ClickAsync();
        await page.Locator("[data-testid=module-Documents]").ClickAsync();
        await page.Locator("[data-testid=save-modules]").ClickAsync();
        await Expect(page.Locator("[data-testid=save-bar]")).ToHaveCountAsync(0);
        await customer.ReloadAsync();
        await Expect(customer.Locator("a.mud-nav-link[href$='documents']")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task A_preset_switches_a_set_of_modules_and_the_changes_can_be_discarded()
    {
        var name = $"Preset Co {Guid.NewGuid():N}"[..20];
        await host.NewWorkspaceAsync(company: name);
        var page = await OwnerPageAsync();
        await OpenCompanyAsync(page, name);
        await page.Locator("[data-testid=tab-modules]").ClickAsync();

        await page.Locator("[data-testid=preset-sales]").ClickAsync();
        await Expect(page.Locator("[data-testid=module-Invoices]")).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(page.Locator("[data-testid=module-Deals]")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator("[data-testid=save-bar]")).ToBeVisibleAsync();

        await page.Locator("[data-testid=discard]").ClickAsync();
        await Expect(page.Locator("[data-testid=save-bar]")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid=module-Invoices]")).ToHaveAttributeAsync("aria-pressed", "true");

        // Roadmap modules are listed but cannot be switched on, core modules cannot be switched off.
        await Expect(page.Locator("[data-testid=module-Tickets]")).ToBeDisabledAsync();
        await Expect(page.Locator("[data-testid=module-Dashboard]")).ToBeDisabledAsync();
    }

    [E2EFact]
    public async Task The_owner_changes_a_users_limit_a_plan_and_a_status_and_keeps_a_note()
    {
        var name = $"Limits Co {Guid.NewGuid():N}"[..20];
        var workspace = await host.NewWorkspaceAsync(company: name);
        var page = await OwnerPageAsync();
        await OpenCompanyAsync(page, name);

        await page.Locator("[data-testid=tab-plan]").ClickAsync();
        await page.Locator("input[data-testid=max-users]").FillAsync("3");
        await page.Locator("textarea[data-testid=notes]").FillAsync("Promised extra seats at the renewal call.");
        await page.Locator("[data-testid=trial-14]").ClickAsync();
        await Shots.SaveAsync(page, "platform-company-plan");
        await page.Locator("[data-testid=save-limits]").ClickAsync();
        await page.Locator("[data-testid=tab-history]").ClickAsync();
        await Expect(page.Locator("[data-testid=history]")).ToContainTextAsync("User limit set to 3");

        await page.Locator("[data-testid=tab-plan]").ClickAsync();
        await page.Locator("[data-testid=plan-starter]").ClickAsync();
        await Expect(page.Locator("[data-testid=plan-starter]")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-current"));

        await page.Locator("[data-testid=status-menu]").ClickAsync();
        await page.Locator("[data-testid=set-status-Suspended]").ClickAsync();
        await Expect(page.Locator("[data-testid=company-hero] [data-testid=status-pill]")).ToHaveTextAsync("Suspended");

        // A suspended company cannot sign in.
        using var http = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(workspace.Email, workspace.Password));
        Assert.False(login.IsSuccessStatusCode);
    }

    [E2EFact]
    public async Task The_catalogue_plans_and_activity_pages_show_the_whole_platform()
    {
        var name = $"Catalogue Co {Guid.NewGuid():N}"[..20];
        await host.NewWorkspaceAsync(company: name);
        var page = await OwnerPageAsync();

        await page.GotoAsync("/platform/modules");
        await Expect(page.Locator("[data-testid=catalogue-Documents]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=catalogue-Tickets]")).ToContainTextAsync("Coming soon");
        await Shots.SaveAsync(page, "platform-modules");

        await page.GotoAsync("/platform/plans");
        await Expect(page.Locator("[data-testid=plan-list]")).ToBeVisibleAsync();
        await page.Locator("[data-testid=plan-card-starter]").ClickAsync();
        await page.Locator("input[data-testid=plan-price]").FillAsync("19");
        await page.Locator("[data-testid=plan-save]").ClickAsync();
        await Expect(page.Locator("[data-testid=plan-card-starter]")).ToContainTextAsync("$19");
        await Shots.SaveAsync(page, "platform-plans");

        await page.GotoAsync("/platform/activity");
        await Expect(page.Locator("[data-testid=activity-list]")).ToContainTextAsync("Starter");
    }

    [E2EFact]
    public async Task The_company_list_filters_by_status_and_switches_to_a_table()
    {
        var name = $"Filter Co {Guid.NewGuid():N}"[..20];
        await host.NewWorkspaceAsync(company: name);
        var page = await OwnerPageAsync();
        await page.GotoAsync("/platform/companies");
        await Expect(page.Locator("[data-testid=company-card]").First).ToBeVisibleAsync();
        await Task.Delay(900);
        await Shots.SaveAsync(page, "platform-companies");

        await page.Locator("[data-testid=status-Trial]").ClickAsync();
        await Expect(page.Locator("[data-testid=company-card]").Filter(new LocatorFilterOptions { HasText = name })).ToHaveCountAsync(1);
        await page.Locator("[data-testid=view-table]").ClickAsync();
        await Expect(page.Locator("[data-testid=company-row]").Filter(new LocatorFilterOptions { HasText = name })).ToHaveCountAsync(1);
        await Shots.SaveAsync(page, "platform-companies-table");
    }

    [E2EFact]
    public async Task The_menu_button_hides_and_shows_the_side_menu_on_a_wide_screen_and_opens_it_on_a_phone()
    {
        var page = await OwnerPageAsync();
        var rail = page.Locator("aside.pc-rail");
        await Expect(rail).ToBeInViewportAsync();
        await page.Locator("[data-testid=platform-menu]").ClickAsync();
        await Expect(rail).Not.ToBeInViewportAsync();
        await page.Locator("[data-testid=platform-menu]").ClickAsync();
        await Expect(rail).ToBeInViewportAsync();

        var phone = await OwnerPageAsync(390);
        await Expect(phone.Locator("aside.pc-rail")).Not.ToBeInViewportAsync();
        await phone.Locator("[data-testid=platform-menu]").ClickAsync();
        await Expect(phone.Locator("aside.pc-rail")).ToBeInViewportAsync();
        await phone.Locator("aside.pc-rail a[href$='platform/companies']").ClickAsync();
        await Expect(phone).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/platform/companies$"));
        await Expect(phone.Locator("aside.pc-rail")).Not.ToBeInViewportAsync();
    }

    [E2EFact]
    public async Task The_owner_onboards_a_company_and_its_owner_can_sign_in()
    {
        var name = $"Onboarded {Guid.NewGuid():N}"[..20];
        var email = $"new-{Guid.NewGuid():N}@onboard.test";
        var page = await OwnerPageAsync();
        await page.GotoAsync("/platform/companies");
        await page.Locator("[data-testid=new-company]").ClickAsync();
        await page.Locator("input[data-testid=nc-name]").FillAsync(name);
        await page.Locator("input[data-testid=nc-first]").FillAsync("Lina");
        await page.Locator("input[data-testid=nc-last]").FillAsync("Saleh");
        await page.Locator("input[data-testid=nc-email]").FillAsync(email);
        await page.Locator("input[data-testid=nc-password]").FillAsync("Welcome#2026");
        await page.Locator("select[data-testid=nc-preset]").SelectOptionAsync("sales");
        await Shots.SaveAsync(page, "platform-new-company");
        await page.Locator("[data-testid=nc-save]").ClickAsync();

        await Expect(page.Locator("[data-testid=company-name]")).ToHaveTextAsync(name);
        await page.Locator("[data-testid=tab-modules]").ClickAsync();
        await Expect(page.Locator("[data-testid=module-Invoices]")).ToHaveAttributeAsync("aria-pressed", "false");

        var owner = await host.NewPageAsync();
        await E2EHost.LoginAsync(owner, new Workspace(email, "Welcome#2026", ""));
        await Expect(owner).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }

    [E2EFact]
    public async Task A_mistake_in_the_onboarding_form_is_explained()
    {
        var page = await OwnerPageAsync();
        await page.GotoAsync("/platform/companies");
        await page.Locator("[data-testid=new-company]").ClickAsync();
        await page.Locator("[data-testid=nc-save]").ClickAsync();
        await Expect(page.Locator("[data-testid=nc-error]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=new-company-panel]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task The_owner_builds_a_plan_with_chosen_modules_and_a_company_on_it_gets_only_those()
    {
        var name = $"Planned Co {Guid.NewGuid():N}"[..20];
        var planName = $"Lean {Guid.NewGuid():N}"[..12];
        var workspace = await host.NewWorkspaceAsync(company: name);
        var customer = await host.NewPageAsync();
        await E2EHost.LoginAsync(customer, workspace);
        await Expect(customer.Locator("a.mud-nav-link[href$='invoices']")).ToBeVisibleAsync();

        var page = await OwnerPageAsync();
        await page.GotoAsync("/platform/plans");
        await page.Locator("[data-testid=new-plan]").ClickAsync();
        await page.Locator("input[data-testid=plan-name]").FillAsync(planName);
        await page.Locator("input[data-testid=plan-description]").FillAsync("Sales only");
        await page.Locator("input[data-testid=plan-price]").FillAsync("15");
        await page.Locator("[data-testid=plan-none]").ClickAsync();
        await page.Locator("input[data-testid=plan-mod-Leads]").CheckAsync();
        await page.Locator("input[data-testid=plan-mod-Contacts]").CheckAsync();
        await Expect(page.Locator("[data-testid=plan-module-count]")).ToContainTextAsync("2 of");
        await Shots.SaveAsync(page, "platform-plan-editor");
        await page.Locator("[data-testid=plan-save]").ClickAsync();
        await Expect(page.Locator("[data-testid=plan-list]")).ToContainTextAsync(planName);
        await Expect(page.Locator("[data-testid=plan-list]")).ToContainTextAsync("2 of");

        await OpenCompanyAsync(page, name);
        await page.Locator("[data-testid=tab-plan]").ClickAsync();
        await page.Locator("[data-testid=plan-cards] > div").Filter(new LocatorFilterOptions { HasText = planName }).ClickAsync();
        await page.Locator("[data-testid=tab-modules]").ClickAsync();
        await Expect(page.Locator("[data-testid=module-Invoices]")).ToBeDisabledAsync();
        await Expect(page.Locator("[data-testid=module-Invoices]")).ToContainTextAsync("Not in the");
        await Expect(page.Locator("[data-testid=module-Leads]")).ToHaveAttributeAsync("aria-pressed", "true");

        await customer.ReloadAsync();
        await Expect(customer.Locator("a.mud-nav-link[href$='invoices']")).ToHaveCountAsync(0);
        await Expect(customer.Locator("a.mud-nav-link[href$='leads']")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task Modules_can_be_seen_as_a_table_and_as_a_grid_of_who_has_what()
    {
        var name = $"Grid Co {Guid.NewGuid():N}"[..20];
        await host.NewWorkspaceAsync(company: name);
        var page = await OwnerPageAsync();
        await page.GotoAsync("/platform/modules");
        await page.Locator("[data-testid=mod-view-table]").ClickAsync();
        await Expect(page.Locator("[data-testid=module-row-Documents]")).ToContainTextAsync("Optional");
        await Expect(page.Locator("[data-testid=module-row-Dashboard]")).ToContainTextAsync("Always on");
        await Expect(page.Locator("[data-testid=module-row-Tickets]")).ToContainTextAsync("Coming soon");
        await Shots.SaveAsync(page, "platform-modules-table");

        await page.Locator("[data-testid=mod-view-matrix]").ClickAsync();
        await Expect(page.Locator("[data-testid=matrix-row]").Filter(new LocatorFilterOptions { HasText = name })).ToHaveCountAsync(1);
        await Shots.SaveAsync(page, "platform-modules-matrix");
    }

    [E2EFact]
    public async Task The_audit_trail_filters_opens_a_row_and_exports()
    {
        var name = $"Trail Co {Guid.NewGuid():N}"[..20];
        var workspace = await host.NewWorkspaceAsync(company: name);
        var page = await OwnerPageAsync();
        await OpenCompanyAsync(page, name);
        await page.Locator("[data-testid=tab-modules]").ClickAsync();
        await page.Locator("[data-testid=module-Documents]").ClickAsync();
        await page.Locator("[data-testid=save-modules]").ClickAsync();
        await Expect(page.Locator("[data-testid=save-bar]")).ToHaveCountAsync(0);

        await page.GotoAsync("/platform/activity");
        await Expect(page.Locator("[data-testid=audit-row]").First).ToBeVisibleAsync();
        await page.Locator("select[data-testid=audit-company]").SelectOptionAsync(new SelectOptionValue { Label = name });
        await Expect(page.Locator("[data-testid=audit-row]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=audit-row]")).ToContainTextAsync("Priya Owner");
        await page.Locator("[data-testid=audit-row]").ClickAsync();
        await Expect(page.Locator("[data-testid=audit-detail]")).ToContainTextAsync("modules.changed");
        await Shots.SaveAsync(page, "platform-audit-trail");

        await page.Locator("[data-testid=audit-kind-plan]").ClickAsync();
        await Expect(page.Locator("[data-testid=audit-row]")).ToHaveCountAsync(0);
        await page.Locator("[data-testid=audit-clear]").ClickAsync();
        await Expect(page.Locator("[data-testid=audit-row]").First).ToBeVisibleAsync();

        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("[data-testid=audit-export]").ClickAsync());
        Assert.EndsWith(".csv", download.SuggestedFilename);
    }

    [E2EFact]
    public async Task The_bell_tells_the_owner_about_new_companies_and_remembers_what_was_read()
    {
        var name = $"Bell Co {Guid.NewGuid():N}"[..20];
        await host.NewWorkspaceAsync(company: name);
        var page = await OwnerPageAsync();
        await Expect(page.Locator("[data-testid=bell-count]")).ToBeVisibleAsync();
        await page.Locator("[data-testid=bell]").ClickAsync();
        await Expect(page.Locator("[data-testid=bell-panel]")).ToContainTextAsync(name);
        await Shots.SaveAsync(page, "platform-bell");
        await page.Locator("[data-testid=mark-all-read]").ClickAsync();
        await Expect(page.Locator("[data-testid=bell-count]")).ToHaveCountAsync(0);

        await page.ReloadAsync();
        await Expect(page.Locator("[data-testid=kpi-companies]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=bell-count]")).ToHaveCountAsync(0);

        await page.GotoAsync("/platform/notifications");
        await Expect(page.Locator("[data-testid=notes-list]")).ToContainTextAsync(name);
        await page.Locator("[data-testid=notes-unread]").ClickAsync();
        await Expect(page.Locator("[data-testid=notes-item]")).ToHaveCountAsync(0);
    }
}
