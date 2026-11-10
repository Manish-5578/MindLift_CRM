using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Activities;

/// <summary>The record timeline also shows tickets and what other features recorded (merges), without repeating creation twice.</summary>
public sealed class TimelineEnrichmentTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task<List<TimelineEntryDto>> TimelineAsync(Session s, string type, Guid id)
        => (await s.Client.GetFromJsonAsync<List<TimelineEntryDto>>($"/api/v1/timeline?entityType={type}&entityId={id}"))!;

    private static async Task<AccountDto> AccountAsync(Session s, string name)
        => (await (await s.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest(name, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<AccountDto>())!;

    [Fact]
    public async Task An_account_timeline_lists_its_tickets_and_a_contact_timeline_lists_theirs()
    {
        var s = await Workspaces.SignupAsync(factory);
        var account = await AccountAsync(s, "Tyrell Corp");
        var contact = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Rachael", "Tyrell", null, null, null, null, account.Id, null, null, null))).Content.ReadFromJsonAsync<ContactDto>();
        var forAccount = await (await s.Client.PostAsJsonAsync("/api/v1/tickets", new SaveTicketRequest("Replicant recall", "x", null, null, null, account.Id, null))).Content.ReadFromJsonAsync<TicketDto>();
        var forContact = await (await s.Client.PostAsJsonAsync("/api/v1/tickets", new SaveTicketRequest("Owl is unwell", "x", null, null, contact!.Id, null, null))).Content.ReadFromJsonAsync<TicketDto>();

        var accountEntries = await TimelineAsync(s, "accounts", account.Id);
        var accountTicket = accountEntries.Single(e => e.Kind == "ticket");
        accountTicket.Title.ShouldContain(forAccount!.Number);
        accountTicket.Title.ShouldContain("Replicant recall");
        accountTicket.Href.ShouldBe($"/tickets/{forAccount.Id}");

        var contactTicket = (await TimelineAsync(s, "contacts", contact.Id)).Single(e => e.Kind == "ticket");
        contactTicket.Href.ShouldBe($"/tickets/{forContact!.Id}");
    }

    [Fact]
    public async Task Another_companys_tickets_never_appear_on_my_timeline()
    {
        var mine = await Workspaces.SignupAsync(factory);
        var account = await AccountAsync(mine, "Weyland");
        var other = await Workspaces.SignupAsync(factory);
        (await other.Client.PostAsJsonAsync("/api/v1/tickets", new SaveTicketRequest("Not mine", "x", null, null, null, null, null))).EnsureSuccessStatusCode();

        (await TimelineAsync(mine, "accounts", account.Id)).ShouldNotContain(e => e.Kind == "ticket");
    }

    [Fact]
    public async Task A_merge_shows_on_the_timeline_of_the_account_that_stays_and_creation_is_not_listed_twice()
    {
        var s = await Workspaces.SignupAsync(factory);
        var master = await AccountAsync(s, "Cyberdyne Systems");
        var twin = await AccountAsync(s, "Cyberdyne Systems Inc");

        (await s.Client.PostAsJsonAsync($"/api/v1/accounts/{master.Id}/merge", new MergeAccountsRequest([twin.Id]))).EnsureSuccessStatusCode();

        var entries = await TimelineAsync(s, "accounts", master.Id);
        var merged = entries.Single(e => e.Kind == "event");
        merged.Title.ShouldContain("Cyberdyne Systems Inc");
        merged.ActivityType.ShouldBe("account_merged");
        entries.Count(e => e.Kind == "created").ShouldBe(1);

        var contact = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Miles", "Dyson", null, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<ContactDto>();
        (await TimelineAsync(s, "contacts", contact!.Id)).Count(e => e.Kind == "created").ShouldBe(1);
    }
}
