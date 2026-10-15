using System.Net.Http.Json;
using System.Text;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>Deals on the list, the board and the detail page, used the way a person uses them.</summary>
[Collection(E2ECollection.Name)]
public sealed class DealsE2ETests(E2EHost host)
{
    private async Task<(IPage Page, HttpClient Api, PipelineDto Pipeline)> StartAsync(int width = 1440)
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync(width);
        await E2EHost.LoginAsync(page, workspace);
        var api = host.ApiFor(workspace);
        var pipelines = (await api.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!;
        return (page, api, pipelines.First(p => p.IsDefault));
    }

    private static async Task<DealDto> CreateDealAsync(HttpClient api, string name, decimal amount = 1000)
    {
        var response = await api.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest(name, null, null, amount, null, null, null, null, null, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DealDto>())!;
    }

    private static ILocator Drawer(IPage page) => page.Locator("[data-testid=form-drawer].mud-drawer--open");

    private static LocatorDragToOptions FromEdge => new() { SourcePosition = new SourcePosition { X = 4, Y = 4 } };

    private static string ColumnOf(PipelineStageDto stage) => $"[data-testid=kanban-column-{stage.Id}]";

    [E2EFact]
    public async Task A_deal_can_be_created_from_the_list_with_a_company_and_appears_in_it()
    {
        var (page, api, _) = await StartAsync();
        await api.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Acme Retail", null, null, null, null, null, null, null));
        await page.GotoAsync("/deals");
        await Expect(page.Locator("[data-testid=entity-list]")).ToContainTextAsync("No deals yet");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        var drawer = Drawer(page);
        await drawer.Locator("input[data-testid=deal-name]").FillAsync("Fit-out");
        await drawer.Locator("input[data-testid=deal-amount]").FillAsync("12500");
        await drawer.Locator("input[data-testid=deal-account]").FillAsync("acm");
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Acme Retail" }).ClickAsync();
        await drawer.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create deal" }).ClickAsync();

        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=row]")).ToContainTextAsync("Fit-out");
        await Expect(page.Locator("[data-testid=row]")).ToContainTextAsync("Acme Retail");
        await Shots.SaveAsync(page, "deals-list");
    }

    [E2EFact]
    public async Task A_deal_needs_a_name()
    {
        var (page, _, _) = await StartAsync();
        await page.GotoAsync("/deals");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create deal" }).ClickAsync();

        await Expect(Drawer(page)).ToContainTextAsync("Enter the deal name.");
    }

    [E2EFact]
    public async Task Dragging_a_card_to_the_next_stage_moves_it_and_updates_both_totals()
    {
        var (page, api, pipeline) = await StartAsync();
        await CreateDealAsync(api, "Mover", 4000);
        var open = pipeline.Stages.Where(s => s.StageType == "Open").OrderBy(s => s.SortOrder).ToList();
        await page.GotoAsync("/deals/board");
        await Expect(page.Locator($"{ColumnOf(open[0])} [data-testid=kanban-card]")).ToHaveCountAsync(1);

        await page.Locator($"{ColumnOf(open[0])} [data-testid=kanban-card]").First.DragToAsync(page.Locator(ColumnOf(open[1])), FromEdge);

        await Expect(page.Locator($"{ColumnOf(open[1])} [data-testid=kanban-card]")).ToHaveCountAsync(1);
        await Expect(page.Locator($"{ColumnOf(open[0])} [data-testid=kanban-card]")).ToHaveCountAsync(0);
        await Expect(page.Locator(ColumnOf(open[1]))).ToContainTextAsync("4,000");
        await Shots.SaveAsync(page, "deals-board");
    }

    [E2EFact]
    public async Task Dropping_a_card_on_lost_needs_a_reason_and_marks_the_deal_lost()
    {
        var (page, api, pipeline) = await StartAsync(2600);
        var deal = await CreateDealAsync(api, "Leaver");
        var first = pipeline.Stages.Where(s => s.StageType == "Open").OrderBy(s => s.SortOrder).First();
        var lost = pipeline.Stages.First(s => s.StageType == "Lost");
        await page.GotoAsync("/deals/board");

        await page.Locator($"{ColumnOf(first)} [data-testid=kanban-card]").First.DragToAsync(page.Locator(ColumnOf(lost)), FromEdge);
        await page.Locator("[data-testid=lost-ok]").ClickAsync();
        await Expect(page.GetByText("Choose a reason.")).ToBeVisibleAsync();
        await page.Locator("[data-testid=lost-reason]").ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Price" }).ClickAsync();
        await page.Locator("[data-testid=lost-ok]").ClickAsync();

        await Expect(page.Locator($"{ColumnOf(lost)} [data-testid=kanban-card]")).ToHaveCountAsync(1);
        Assert.Equal("Lost", (await api.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal.Id}"))!.Status);
    }

    [E2EFact]
    public async Task Cancelling_the_lost_dialog_leaves_the_card_where_it_was()
    {
        var (page, api, pipeline) = await StartAsync(2600);
        await CreateDealAsync(api, "Stayer");
        var first = pipeline.Stages.Where(s => s.StageType == "Open").OrderBy(s => s.SortOrder).First();
        var lost = pipeline.Stages.First(s => s.StageType == "Lost");
        await page.GotoAsync("/deals/board");

        await page.Locator($"{ColumnOf(first)} [data-testid=kanban-card]").First.DragToAsync(page.Locator(ColumnOf(lost)), FromEdge);
        await page.Locator("[data-testid=lost-cancel]").ClickAsync();

        await Expect(page.Locator($"{ColumnOf(first)} [data-testid=kanban-card]")).ToHaveCountAsync(1);
    }

    [E2EFact]
    public async Task Dropping_a_card_on_won_asks_first_and_then_closes_the_deal()
    {
        var (page, api, pipeline) = await StartAsync(2600);
        var deal = await CreateDealAsync(api, "Winner");
        var first = pipeline.Stages.Where(s => s.StageType == "Open").OrderBy(s => s.SortOrder).First();
        var won = pipeline.Stages.First(s => s.StageType == "Won");
        await page.GotoAsync("/deals/board");

        await page.Locator($"{ColumnOf(first)} [data-testid=kanban-card]").First.DragToAsync(page.Locator(ColumnOf(won)), FromEdge);
        await page.Locator("[data-testid=confirm-ok]").ClickAsync();

        await Expect(page.Locator($"{ColumnOf(won)} [data-testid=kanban-card]")).ToHaveCountAsync(1);
        var saved = (await api.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal.Id}"))!;
        Assert.Equal("Won", saved.Status);
        Assert.Equal(100, saved.Probability);
    }


    [E2EFact]
    public async Task On_a_phone_a_deal_is_moved_to_lost_with_the_card_menu_and_needs_a_reason()
    {
        var (page, api, pipeline) = await StartAsync(375);
        var deal = await CreateDealAsync(api, "Phone deal");
        var lost = pipeline.Stages.First(s => s.StageType == "Lost");
        await page.GotoAsync("/deals/board");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Move this card" }).ClickAsync();
        await page.Locator($"[data-testid=kanban-move-{lost.Id}]").ClickAsync();
        await page.Locator("[data-testid=lost-ok]").ClickAsync();
        await Expect(page.GetByText("Choose a reason.")).ToBeVisibleAsync();
        await page.Locator("[data-testid=lost-reason]").ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Timing" }).ClickAsync();
        await page.Locator("[data-testid=lost-ok]").ClickAsync();

        await Expect(page.GetByText("Phone deal moved to Lost")).ToBeVisibleAsync();
        Assert.Equal("Lost", (await api.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal.Id}"))!.Status);
    }
    [E2EFact]
    public async Task The_detail_page_stage_bar_moves_the_deal_and_the_history_shows_it()
    {
        var (page, api, pipeline) = await StartAsync();
        var deal = await CreateDealAsync(api, "Stepper");
        var second = pipeline.Stages.Where(s => s.StageType == "Open").OrderBy(s => s.SortOrder).ElementAt(1);
        await page.GotoAsync($"/deals/{deal.Id}");

        await page.Locator($"[data-testid=stage-{second.Id}]").ClickAsync();

        await Expect(page.Locator("[data-testid=timeline] [data-testid=timeline-item]")).ToHaveCountAsync(2);
        await Expect(page.Locator("[data-testid=timeline]")).ToContainTextAsync($"→ {second.Name}");
        await Shots.SaveAsync(page, "deal-detail");
    }

    [E2EFact]
    public async Task Marking_a_deal_lost_from_the_detail_page_shows_the_reason()
    {
        var (page, api, pipeline) = await StartAsync();
        var deal = await CreateDealAsync(api, "Gone");
        var lost = pipeline.Stages.First(s => s.StageType == "Lost");
        await page.GotoAsync($"/deals/{deal.Id}");

        await page.Locator($"[data-testid=stage-{lost.Id}]").ClickAsync();
        await page.Locator("[data-testid=lost-reason]").ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Competitor" }).ClickAsync();
        await page.Locator("[data-testid=lost-notes]").FillAsync("Went with a rival");
        await page.Locator("[data-testid=lost-ok]").ClickAsync();

        await Expect(page.Locator("[data-testid=lost-reason-text]")).ToContainTextAsync("Competitor");
        await Expect(page.Locator("[data-testid=lost-reason-text]")).ToContainTextAsync("Went with a rival");
    }

    [E2EFact]
    public async Task A_deal_can_be_edited_and_deleted_from_its_page()
    {
        var (page, api, _) = await StartAsync();
        var deal = await CreateDealAsync(api, "Editable", 100);
        await page.GotoAsync($"/deals/{deal.Id}");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=deal-amount]").FillAsync("999");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save changes" }).ClickAsync();
        await Expect(page.Locator("[data-testid=deal-details]")).ToContainTextAsync("999");

        await page.Locator("[data-testid=delete-deal]").ClickAsync();
        await page.Locator("[data-testid=confirm-ok]").ClickAsync();
        await page.WaitForURLAsync("**/deals");
        await Expect(page.Locator("[data-testid=entity-list]")).ToContainTextAsync("No deals yet");
    }

    [E2EFact]
    public async Task Deals_can_be_imported_from_a_csv_file()
    {
        var (page, _, _) = await StartAsync();
        await page.GotoAsync("/deals/import");

        await page.Locator("[data-testid=file-dropzone] input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "deals.csv",
            MimeType = "text/csv",
            Buffer = Encoding.UTF8.GetBytes("Name,Amount,Expected close\nBig one,50000,2027-02-01\nSmall one,500,\n"),
        });
        await Expect(page.Locator("[data-testid=import-step-check]")).ToContainTextAsync("2 rows found");
        await page.Locator("[data-testid=import-submit]").ClickAsync();
        await Expect(page.Locator("[data-testid=import-step-result]")).ToContainTextAsync("2 imported");
        await page.Locator("[data-testid=import-done]").ClickAsync();

        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(2);
    }

    [E2EFact]
    public async Task Deals_can_be_exported_to_a_csv_file()
    {
        var (page, api, _) = await StartAsync();
        await CreateDealAsync(api, "Export Me");
        await page.GotoAsync("/deals");
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);

        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("[data-testid=export-deals]").ClickAsync());

        Assert.Equal("deals.csv", download.SuggestedFilename);
        Assert.Contains("Export Me", await File.ReadAllTextAsync((await download.PathAsync())!));
    }
}
