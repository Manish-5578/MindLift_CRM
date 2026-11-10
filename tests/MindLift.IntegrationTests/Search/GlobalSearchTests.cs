using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Search;

/// <summary>The search box at the top of every page also finds deals and tickets, and keeps workspaces apart.</summary>
public sealed class GlobalSearchTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task NewDealAsync(Session s, string name)
    {
        var ind = await s.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"));
        if (ind.StatusCode != HttpStatusCode.Conflict) ind.EnsureSuccessStatusCode();
        (await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest(name, null, null, 12000m, null, 50, DateTime.UtcNow.AddDays(10), null, null, null, null, null))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_search_box_finds_deals_and_tickets()
    {
        var s = await Workspaces.SignupAsync(factory);
        await NewDealAsync(s, "Quartzite Renewal");
        (await s.Client.PostAsJsonAsync("/api/v1/tickets", new SaveTicketRequest("Quartzite invoice is wrong", "Details", null, null, null, null, null))).EnsureSuccessStatusCode();

        var hits = (await s.Client.GetFromJsonAsync<List<SearchHitDto>>("/api/v1/search?q=quartz"))!;
        hits.Select(h => h.Kind).Order().ToList().ShouldBe(["deal", "ticket"]);
        hits.Single(h => h.Kind == "ticket").Title.ShouldContain("T-");
        hits.Single(h => h.Kind == "deal").Title.ShouldBe("Quartzite Renewal");
    }

    [Fact]
    public async Task A_ticket_is_found_by_its_number()
    {
        var s = await Workspaces.SignupAsync(factory);
        var created = await (await s.Client.PostAsJsonAsync("/api/v1/tickets", new SaveTicketRequest("Printer jam", "Details", null, null, null, null, null))).Content.ReadFromJsonAsync<TicketDto>();
        var hits = (await s.Client.GetFromJsonAsync<List<SearchHitDto>>("/api/v1/search?q=" + Uri.EscapeDataString(created!.Number)))!;
        hits.ShouldContain(h => h.Kind == "ticket" && h.Id == created.Id);
    }

    [Fact]
    public async Task Another_workspace_never_sees_my_deals_or_tickets_in_search()
    {
        var mine = await Workspaces.SignupAsync(factory);
        await NewDealAsync(mine, "Obsidian Hush Deal");
        (await mine.Client.PostAsJsonAsync("/api/v1/tickets", new SaveTicketRequest("Obsidian Hush ticket", "Details", null, null, null, null, null))).EnsureSuccessStatusCode();
        var other = await Workspaces.SignupAsync(factory);
        (await other.Client.GetFromJsonAsync<List<SearchHitDto>>("/api/v1/search?q=obsidian"))!.ShouldBeEmpty();
    }
}
