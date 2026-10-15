using System.Net.Http.Json;
using System.Text;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>The lead screens used the way a person uses them: forms, filters, the board, import, export, merge and convert.</summary>
[Collection(E2ECollection.Name)]
public sealed class LeadsE2ETests(E2EHost host)
{
    private async Task<(IPage Page, Workspace Workspace, HttpClient Api)> StartAsync(int width = 1440)
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync(width);
        await E2EHost.LoginAsync(page, workspace);
        return (page, workspace, host.ApiFor(workspace));
    }

    private static async Task<LeadDto> CreateLeadAsync(HttpClient api, string first, string last = "Tester", string? email = null, string? company = null, string status = "new")
    {
        var response = await api.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest(first, last, email, null, company, status, "website", null, null, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LeadDto>())!;
    }

    private static ILocator Drawer(IPage page) => page.Locator(".ml-form-drawer").First;

    // ---------- list ----------

    [E2EFact]
    public async Task A_lead_can_be_created_from_the_list_and_appears_in_it()
    {
        var (page, _, _) = await StartAsync();
        await page.GotoAsync("/leads");
        await Expect(page.Locator("[data-testid=entity-list]")).ToContainTextAsync("No leads yet");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        var drawer = page.Locator("[data-testid=form-drawer].mud-drawer--open");
        await drawer.GetByLabel("First name").FillAsync("Nadia");
        await drawer.GetByLabel("Last name").FillAsync("Hassan");
        await drawer.GetByLabel("Email").FillAsync("nadia@example.test");
        await drawer.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create lead" }).ClickAsync();

        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=row]")).ToContainTextAsync("Nadia Hassan");
        await Shots.SaveAsync(page, "leads-list");
    }

    [E2EFact]
    public async Task A_second_lead_with_the_same_email_is_flagged_and_created_when_confirmed()
    {
        var (page, _, api) = await StartAsync();
        await CreateLeadAsync(api, "Original", email: "same@example.test");
        await page.GotoAsync("/leads");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        var drawer = page.Locator("[data-testid=form-drawer].mud-drawer--open");
        await drawer.GetByLabel("First name").FillAsync("Copy");
        await drawer.GetByLabel("Email").FillAsync("same@example.test");
        var create = drawer.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create lead" });
        await create.ClickAsync();

        await Expect(drawer).ToContainTextAsync("Possible duplicates");
        await Expect(drawer).ToContainTextAsync("Same email");
        await create.ClickAsync();
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(2);
    }

    [E2EFact]
    public async Task The_search_box_narrows_the_list_and_clear_brings_everything_back()
    {
        var (page, _, api) = await StartAsync();
        await CreateLeadAsync(api, "Alice");
        await CreateLeadAsync(api, "Bob");
        await page.GotoAsync("/leads");
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(2);

        await page.Locator("input[data-testid=search]").FillAsync("alic");
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=row]")).ToContainTextAsync("Alice");
        Assert.Contains("q=alic", page.Url);

        await page.Locator("[data-testid=clear-filters]").ClickAsync();
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(2);
        Assert.DoesNotContain("q=", page.Url);
    }

    [E2EFact]
    public async Task A_search_with_no_match_shows_no_results_and_a_way_out()
    {
        var (page, _, api) = await StartAsync();
        await CreateLeadAsync(api, "Alice");
        await page.GotoAsync("/leads");

        await page.Locator("input[data-testid=search]").FillAsync("zzzz");

        await Expect(page.Locator("[data-testid=entity-list]")).ToContainTextAsync("No results");
        await page.Locator("[data-testid=empty-clear]").ClickAsync();
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);
    }

    [E2EFact]
    public async Task The_filter_drawer_filters_the_list_shows_a_chip_and_the_chip_can_be_removed()
    {
        var (page, _, api) = await StartAsync();
        await CreateLeadAsync(api, "Alice");
        await CreateLeadAsync(api, "Bob");
        await page.GotoAsync("/leads");

        await page.Locator("[data-testid=open-filters]").ClickAsync();
        var drawer = page.Locator("[data-testid=filters-drawer]");
        await drawer.Locator("[data-testid=add-filter]").ClickAsync();
        await drawer.Locator("input[data-testid=filter-value]").FillAsync("bob");
        await page.WaitForTimeoutAsync(800);
        await drawer.Locator("[data-testid=filters-apply]").ClickAsync();

        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=row]")).ToContainTextAsync("Bob");
        await Expect(page.Locator("[data-testid=filter-chip]")).ToContainTextAsync("First name contains bob");

        await page.Locator("[data-testid=filter-chip] .mud-chip-close-button").ClickAsync();
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(2);
    }

    [E2EFact]
    public async Task The_filter_and_the_view_are_kept_in_the_address_so_a_reload_shows_the_same_list()
    {
        var (page, _, api) = await StartAsync();
        await CreateLeadAsync(api, "Alice");
        await CreateLeadAsync(api, "Bob");
        await page.GotoAsync("/leads?q=bob");
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);

        await page.ReloadAsync();

        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);
        await Expect(page.Locator("input[data-testid=search]")).ToHaveValueAsync("bob");
    }

    [E2EFact]
    public async Task Clicking_a_column_heading_sorts_the_list()
    {
        var (page, _, api) = await StartAsync();
        await CreateLeadAsync(api, "Charlie");
        await CreateLeadAsync(api, "Alice");
        await CreateLeadAsync(api, "Bob");
        await page.GotoAsync("/leads");

        await page.Locator("[data-testid=sort-first_name]").ClickAsync();
        await Expect(page.Locator("[data-testid=row]").First).ToContainTextAsync("Alice");

        await page.Locator("[data-testid=sort-first_name]").ClickAsync();
        await Expect(page.Locator("[data-testid=row]").First).ToContainTextAsync("Charlie");
    }

    // ---------- detail ----------

    [E2EFact]
    public async Task A_lead_can_be_opened_edited_and_the_change_shows()
    {
        var (page, _, api) = await StartAsync();
        var lead = await CreateLeadAsync(api, "Edit", company: "Before Co");
        await page.GotoAsync($"/leads/{lead.Id}");
        await Expect(page.GetByText("Before Co").First).ToBeVisibleAsync();

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        var drawer = page.Locator("[data-testid=form-drawer].mud-drawer--open");
        await drawer.GetByLabel("Company").FillAsync("After Co");
        await drawer.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save changes" }).ClickAsync();

        await Expect(page.GetByText("After Co").First).ToBeVisibleAsync();
        await Shots.SaveAsync(page, "lead-detail");
    }

    [E2EFact]
    public async Task Disqualifying_needs_a_reason_and_then_marks_the_lead()
    {
        var (page, _, api) = await StartAsync();
        var lead = await CreateLeadAsync(api, "Cold");
        await page.GotoAsync($"/leads/{lead.Id}");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Disqualify" }).First.ClickAsync();
        await page.Locator("[data-testid=disqualify-submit]").ClickAsync();
        await Expect(page.GetByText("Say why this lead is not a fit.")).ToBeVisibleAsync();

        await page.Locator("input[data-testid=disqualify-reason]").FillAsync("No budget this year");
        await page.Locator("[data-testid=disqualify-submit]").ClickAsync();

        await Expect(page.GetByText("No budget this year").First).ToBeVisibleAsync();
        await Expect(page.GetByText("Disqualified because")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task Converting_a_lead_creates_the_records_and_locks_the_lead()
    {
        var (page, _, api) = await StartAsync();
        var lead = await CreateLeadAsync(api, "Convert", company: "Convert Co");
        await page.GotoAsync($"/leads/{lead.Id}");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Convert" }).First.ClickAsync();
        await page.Locator("[data-testid=convert-submit]").ClickAsync();

        await Expect(page.Locator("[data-testid=converted-banner]")).ToBeVisibleAsync();
        await page.GotoAsync("/leads");
        await Expect(page.Locator("[data-testid=entity-list]")).ToContainTextAsync("No leads yet");
    }

    [E2EFact]
    public async Task Duplicates_can_be_found_and_merged_into_one_lead()
    {
        var (page, _, api) = await StartAsync();
        var original = await CreateLeadAsync(api, "Ivy", "Import", "ivy@example.test");
        await CreateLeadAsync(api, "Ivy", "Import", "ivy@example.test", company: "Ivy Trading");
        await page.GotoAsync($"/leads/{original.Id}");

        await page.Locator("[data-testid=find-duplicates]").ClickAsync();
        await Expect(page.Locator("[data-testid=duplicate-row]")).ToHaveCountAsync(1);

        await page.Locator("[data-testid=merge-submit]").ClickAsync();
        await Expect(page.GetByText("Tick at least one lead to merge.")).ToBeVisibleAsync();

        await page.GetByLabel("Merge Ivy Import").CheckAsync();
        await page.Locator("[data-testid=merge-submit]").ClickAsync();
        await page.Locator("[data-testid=confirm-ok]").ClickAsync();

        await Expect(page.GetByText("Merged 1 lead")).ToBeVisibleAsync();
        await Expect(page.GetByText("Ivy Trading").First).ToBeVisibleAsync();
    }

    // ---------- board ----------

    [E2EFact]
    public async Task Dragging_a_card_to_another_column_moves_the_lead_and_updates_the_counts()
    {
        var (page, _, api) = await StartAsync();
        await CreateLeadAsync(api, "Mover");
        await page.GotoAsync("/leads/board");
        var from = page.Locator("[data-testid=kanban-column-new]");
        var to = page.Locator("[data-testid=kanban-column-working]");
        await Expect(from.Locator("[data-testid=kanban-card]")).ToHaveCountAsync(1);

        await from.Locator("[data-testid=kanban-card]").First.DragToAsync(to, new LocatorDragToOptions { SourcePosition = new SourcePosition { X = 4, Y = 4 } });

        await Expect(page.GetByText("Mover Tester moved to Contacted")).ToBeVisibleAsync();
        await Expect(to.Locator("[data-testid=kanban-card]")).ToHaveCountAsync(1);
        await Expect(from.Locator("[data-testid=kanban-card]")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-testid=kanban-count-working]")).ToHaveTextAsync("1");
        await Shots.SaveAsync(page, "leads-board");
    }

    [E2EFact]
    public async Task Dropping_a_card_on_disqualified_asks_for_a_reason_first()
    {
        var (page, _, api) = await StartAsync();
        await CreateLeadAsync(api, "Leaver");
        await page.GotoAsync("/leads/board");
        var card = page.Locator("[data-testid=kanban-column-new] [data-testid=kanban-card]").First;

        await card.DragToAsync(page.Locator("[data-testid=kanban-column-disqualified]"), new LocatorDragToOptions { SourcePosition = new SourcePosition { X = 4, Y = 4 } });
        await page.Locator("[data-testid=reason-ok]").ClickAsync();
        await Expect(page.GetByText("A reason is required.")).ToBeVisibleAsync();
        await page.Locator("[data-testid=reason-input]").FillAsync("Chose a competitor");
        await page.Locator("[data-testid=reason-ok]").ClickAsync();

        await Expect(page.Locator("[data-testid=kanban-column-disqualified] [data-testid=kanban-card]")).ToHaveCountAsync(1);
    }

    [E2EFact]
    public async Task Cancelling_the_reason_leaves_the_card_where_it_was()
    {
        var (page, _, api) = await StartAsync();
        await CreateLeadAsync(api, "Stayer");
        await page.GotoAsync("/leads/board");

        await page.Locator("[data-testid=kanban-column-new] [data-testid=kanban-card]").First.DragToAsync(page.Locator("[data-testid=kanban-column-disqualified]"), new LocatorDragToOptions { SourcePosition = new SourcePosition { X = 4, Y = 4 } });
        await page.Locator("[data-testid=reason-cancel]").ClickAsync();

        await Expect(page.Locator("[data-testid=kanban-column-new] [data-testid=kanban-card]")).ToHaveCountAsync(1);
    }


    [E2EFact]
    public async Task On_a_phone_a_card_is_moved_with_its_menu_and_the_column_picker_switches_columns()
    {
        var (page, _, api) = await StartAsync(375);
        await CreateLeadAsync(api, "Phoned");
        await page.GotoAsync("/leads/board");
        await Expect(page.Locator("[data-testid=kanban-column-new] [data-testid=kanban-card]")).ToHaveCountAsync(1);

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Move this card" }).ClickAsync();
        await page.Locator("[data-testid=kanban-move-working]").ClickAsync();
        await Expect(page.GetByText("Phoned Tester moved to")).ToBeVisibleAsync();

        await page.Locator("[data-testid=kanban-pick-working]").ClickAsync();
        await Expect(page.Locator("[data-testid=kanban-column-working] [data-testid=kanban-card]")).ToBeVisibleAsync();
        await Shots.SaveAsync(page, "leads-board-phone");
    }
    // ---------- import and export ----------

    [E2EFact]
    public async Task A_csv_file_can_be_checked_and_imported_and_the_leads_appear()
    {
        var (page, _, _) = await StartAsync();
        await page.GotoAsync("/leads/import");
        var csv = "First name,Last name,Email,Favourite colour\nIvy,A,ivy.a@example.test,blue\nJon,B,jon.b@example.test,red\n,NoFirst,nofirst@example.test,green\n";

        await page.Locator("[data-testid=file-dropzone] input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "leads.csv",
            MimeType = "text/csv",
            Buffer = Encoding.UTF8.GetBytes(csv),
        });

        await Expect(page.Locator("[data-testid=import-step-check]")).ToContainTextAsync("3 rows found");
        await Expect(page.Locator("[data-testid=column-match]").Filter(new LocatorFilterOptions { HasText = "Favourite colour" })).ToContainTextAsync("Ignored");
        await page.Locator("[data-testid=import-submit]").ClickAsync();

        var result = page.Locator("[data-testid=import-step-result]");
        await Expect(result).ToContainTextAsync("2 imported");
        await Expect(result).ToContainTextAsync("1 could not be imported");
        await Expect(page.Locator("[data-testid=import-errors]")).ToContainTextAsync("first name is missing");

        await page.Locator("[data-testid=import-done]").ClickAsync();
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(2);
    }

    [E2EFact]
    public async Task A_file_without_a_first_name_column_cannot_be_imported()
    {
        var (page, _, _) = await StartAsync();
        await page.GotoAsync("/leads/import");

        await page.Locator("[data-testid=file-dropzone] input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "bad.csv",
            MimeType = "text/csv",
            Buffer = Encoding.UTF8.GetBytes("Email,Company\nx@example.test,Acme\n"),
        });

        await Expect(page.Locator("[data-testid=import-no-first-name]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task A_file_of_the_wrong_type_is_refused_in_the_drop_zone()
    {
        var (page, _, _) = await StartAsync();
        await page.GotoAsync("/leads/import");

        await page.Locator("[data-testid=file-dropzone] input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "photo.png",
            MimeType = "image/png",
            Buffer = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
        });

        await Expect(page.Locator("[data-testid=file-error]")).ToHaveTextAsync("File type not supported");
    }

    [E2EFact]
    public async Task Export_downloads_a_csv_of_what_the_list_shows()
    {
        var (page, _, api) = await StartAsync();
        await CreateLeadAsync(api, "Keep", "One", "keep@example.test");
        await CreateLeadAsync(api, "Drop", "Two", "drop@example.test");
        await page.GotoAsync("/leads?q=keep");
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);

        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("[data-testid=export-leads]").ClickAsync());

        Assert.Equal("leads.csv", download.SuggestedFilename);
        var text = (await File.ReadAllTextAsync((await download.PathAsync())!)).TrimStart('﻿');
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("id,first_name,last_name,email", lines[0]);
        Assert.Equal(2, lines.Length);
        Assert.Contains("keep@example.test", lines[1]);
        Assert.DoesNotContain("drop@example.test", text);
    }
}
