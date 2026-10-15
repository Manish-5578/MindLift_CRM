using System.Net.Http.Json;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>The settings screens that change things: rules, web forms, teams and branches, pipelines and stages, roles.</summary>
[Collection(E2ECollection.Name)]
public sealed class SettingsWritesE2ETests(E2EHost host)
{
    private async Task<(IPage Page, HttpClient Api)> StartAsync()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        return (page, host.ApiFor(workspace));
    }

    private static ILocator Drawer(IPage page) => page.Locator("[data-testid=form-drawer].mud-drawer--open");

    private static async Task Pick(IPage page, ILocator select, string option)
    {
        await select.ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = option }).First.ClickAsync();
    }

    // ---------- assignment rules ----------

    [E2EFact]
    public async Task An_assignment_rule_can_be_created_with_an_owner_edited_and_deleted()
    {
        var (page, _) = await StartAsync();
        await page.GotoAsync("/settings/assignment-rules");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=rule-name]").FillAsync("Website leads");
        await Pick(page, Drawer(page).Locator("input[data-testid=rule-add-user]"), "Eva Tester");
        await Expect(Drawer(page).Locator("[data-testid=rule-user]")).ToHaveCountAsync(1);
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save rule" }).ClickAsync();

        await Expect(page.Locator("[data-testid=rule-row]")).ToContainTextAsync("Website leads");
        await Shots.SaveAsync(page, "settings-assignment-rules");

        await page.GetByLabel("Edit rule").ClickAsync();
        await Drawer(page).Locator("input[data-testid=rule-name]").FillAsync("Web leads");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save rule" }).ClickAsync();
        await Expect(page.Locator("[data-testid=rule-row]")).ToContainTextAsync("Web leads");

        await page.GetByLabel("Delete rule").ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-testid=rule-row]")).ToHaveCountAsync(0);
    }

    [E2EFact]
    public async Task A_rule_needs_a_name()
    {
        var (page, _) = await StartAsync();
        await page.GotoAsync("/settings/assignment-rules");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save rule" }).ClickAsync();

        await Expect(Drawer(page).Locator(".mud-alert")).ToBeVisibleAsync();
    }

    // ---------- web forms ----------

    [E2EFact]
    public async Task A_web_form_can_be_created_with_chosen_fields_and_shows_its_address()
    {
        var (page, api) = await StartAsync();
        await page.GotoAsync("/settings/web-forms");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=form-name]").FillAsync("Contact us");
        await Drawer(page).Locator("[data-testid=field-phone]").First.ClickAsync();
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save form" }).ClickAsync();

        await Expect(page.Locator("[data-testid=form-endpoint] input, input[data-testid=form-endpoint]").First).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("/api/v1/public/forms/"));
        var forms = await api.GetFromJsonAsync<List<WebFormDto>>("/api/v1/settings/web-forms");
        Assert.Contains("phone", forms!.Single().Fields);
        await Shots.SaveAsync(page, "settings-web-forms");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Edit form" }).ClickAsync();
        await Drawer(page).Locator("input[data-testid=form-name]").FillAsync("Contact page");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save form" }).ClickAsync();
        await Expect(page.GetByText("Contact page").First).ToBeVisibleAsync();
    }

    // ---------- teams and branches ----------

    [E2EFact]
    public async Task A_branch_and_a_team_can_be_created_edited_and_deleted()
    {
        var (page, _) = await StartAsync();
        await page.GotoAsync("/settings/teams");

        await page.Locator("[data-testid=add-branch]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=branch-name]").FillAsync("Dubai office");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save branch" }).ClickAsync();
        await Expect(page.Locator("[data-testid=branch-row]")).ToContainTextAsync("Dubai office");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=team-name]").FillAsync("Sales");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save team" }).ClickAsync();
        await Expect(page.Locator("[data-testid=team-row]")).ToContainTextAsync("Sales");
        await Shots.SaveAsync(page, "settings-teams");

        await page.GetByLabel("Edit team").ClickAsync();
        await Drawer(page).Locator("input[data-testid=team-name]").FillAsync("Field sales");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save team" }).ClickAsync();
        await Expect(page.Locator("[data-testid=team-row]")).ToContainTextAsync("Field sales");

        await page.GetByLabel("Delete team").ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-testid=team-row]")).ToHaveCountAsync(0);

        await page.GetByLabel("Delete branch").ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-testid=branch-row]")).ToHaveCountAsync(0);
    }

    // ---------- pipelines ----------

    [E2EFact]
    public async Task A_pipeline_can_be_created_and_its_stages_added_edited_and_deleted()
    {
        var (page, _) = await StartAsync();
        await page.GotoAsync("/settings/pipelines");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=pipeline-name]").FillAsync("Renewals");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save pipeline" }).ClickAsync();
        var card = page.Locator("[data-testid=pipeline-Renewals]");
        await Expect(card).ToBeVisibleAsync();

        await card.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add stage" }).ClickAsync();
        await Drawer(page).Locator("input[data-testid=stage-name]").FillAsync("Reach out");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save stage" }).ClickAsync();
        var row = card.Locator("[data-testid=stage-row]").Filter(new LocatorFilterOptions { HasText = "Reach out" });
        await Expect(row).ToHaveCountAsync(1);

        await row.GetByLabel("Edit stage").ClickAsync();
        await Drawer(page).Locator("input[data-testid=stage-name]").FillAsync("First touch");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save stage" }).ClickAsync();
        var renamed = card.Locator("[data-testid=stage-row]").Filter(new LocatorFilterOptions { HasText = "First touch" });
        await Expect(renamed).ToHaveCountAsync(1);
        await Shots.SaveAsync(page, "settings-pipelines");

        await renamed.GetByLabel("Delete stage").ClickAsync();
        await renamed.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();
        await Expect(card.Locator("[data-testid=stage-row]").Filter(new LocatorFilterOptions { HasText = "First touch" })).ToHaveCountAsync(0);
    }

    // ---------- roles ----------

    [E2EFact]
    public async Task A_role_can_be_created_and_its_permission_matrix_saved_and_kept()
    {
        var (page, api) = await StartAsync();
        await page.GotoAsync("/settings/roles");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=role-name]").FillAsync("Support");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create role" }).ClickAsync();
        await page.Locator("[data-testid=role-row]").Filter(new LocatorFilterOptions { HasText = "Support" }).ClickAsync();
        await Expect(page.Locator("[data-testid=matrix-table]")).ToBeVisibleAsync();

        var firstRow = page.Locator("[data-testid=matrix-row]").First;
        var before = await firstRow.Locator("input[type=checkbox]").Nth(2).IsCheckedAsync();
        await firstRow.Locator("input[type=checkbox]").Nth(2).SetCheckedAsync(!before);
        await page.Locator("[data-testid=save-matrix]").ClickAsync();
        await Expect(page.GetByText("Permissions saved")).ToBeVisibleAsync();

        await page.ReloadAsync();
        await page.Locator("[data-testid=role-row]").Filter(new LocatorFilterOptions { HasText = "Support" }).ClickAsync();
        await Expect(page.Locator("[data-testid=matrix-row]").First.Locator("input[type=checkbox]").Nth(2)).ToBeCheckedAsync(new LocatorAssertionsToBeCheckedOptions { Checked = !before });
        await Shots.SaveAsync(page, "settings-roles");
    }
}
