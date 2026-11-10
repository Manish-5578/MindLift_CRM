using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Duplicates;

/// <summary>Finding duplicate accounts (same name, website or phone) and merging them: everything moves to the account that stays.</summary>
public sealed class AccountDuplicateTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task<AccountDto> NewAccountAsync(Session s, string name, string? website = null, string? phone = null, string? industry = null, Guid? parent = null)
    {
        var r = await s.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest(name, website, phone, industry, null, parent, null, null));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<AccountDto>())!;
    }

    private static async Task<List<DuplicateCandidateDto>> CheckAsync(Session s, string name, string? website = null, string? phone = null)
    {
        var r = await s.Client.PostAsJsonAsync("/api/v1/accounts/check-duplicates", new CheckAccountDuplicatesRequest(name, website, phone));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<List<DuplicateCandidateDto>>())!;
    }

    private static async Task<HttpResponseMessage> MergeAsync(Session s, Guid master, params Guid[] duplicates)
        => await s.Client.PostAsJsonAsync($"/api/v1/accounts/{master}/merge", new MergeAccountsRequest(duplicates));

    [Fact]
    public async Task A_new_account_is_checked_against_existing_names_websites_and_phones()
    {
        var s = await Workspaces.SignupAsync(factory);
        var acme = await NewAccountAsync(s, "Acme Trading Ltd.", "https://www.acme-trading.test/about", "+971 4 555 0101");
        await NewAccountAsync(s, "Totally Different Co", "other.test", "+971 4 555 9999");

        (await CheckAsync(s, "ACME TRADING")).ShouldHaveSingleItem().Reason.ShouldBe("Same name");
        (await CheckAsync(s, "Brand New Name", "acme-trading.test")).ShouldHaveSingleItem().Reason.ShouldBe("Same website");
        (await CheckAsync(s, "Brand New Name", null, "+971 (4) 555-0101")).Single().Id.ShouldBe(acme.Id);
        (await CheckAsync(s, "Nobody Like This", "nowhere.test", "+1 555 000 0000")).ShouldBeEmpty();
        (await CheckAsync(s, "  ", null, null)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_account_lists_its_duplicates_but_never_itself()
    {
        var s = await Workspaces.SignupAsync(factory);
        var a = await NewAccountAsync(s, "Globex Corporation", "globex.test");
        var b = await NewAccountAsync(s, "GLOBEX", "globex.test");
        await NewAccountAsync(s, "Initech");

        var dups = (await s.Client.GetFromJsonAsync<List<DuplicateCandidateDto>>($"/api/v1/accounts/{a.Id}/duplicates"))!;
        dups.Select(d => d.Id).ShouldBe([b.Id]);
        (await s.Client.GetFromJsonAsync<List<DuplicateCandidateDto>>($"/api/v1/accounts/{b.Id}/duplicates"))!.Select(d => d.Id).ShouldBe([a.Id]);
        (await s.Client.GetAsync($"/api/v1/accounts/{Guid.NewGuid()}/duplicates")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Merging_moves_contacts_deals_tickets_and_child_accounts_to_the_account_that_stays()
    {
        var s = await Workspaces.SignupAsync(factory);
        var master = await NewAccountAsync(s, "Umbrella Group");
        var dup = await NewAccountAsync(s, "Umbrella Group Inc", "umbrella.test", "+44 20 7946 0000", "Pharma");
        var child = await NewAccountAsync(s, "Umbrella Labs", parent: dup.Id);

        var contact = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Alice", "Wesker", "alice@umbrella.test", null, null, null, dup.Id, null, null, null))).Content.ReadFromJsonAsync<ContactDto>();
        await s.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"));
        var deal = await (await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Umbrella deal", null, null, 9000m, null, 40, DateTime.UtcNow.AddDays(9), dup.Id, null, null, null, null))).Content.ReadFromJsonAsync<DealDto>();
        var ticket = await (await s.Client.PostAsJsonAsync("/api/v1/tickets", new SaveTicketRequest("Umbrella ticket", "x", null, null, null, dup.Id, null))).Content.ReadFromJsonAsync<TicketDto>();

        var response = await MergeAsync(s, master.Id, dup.Id);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var merged = (await response.Content.ReadFromJsonAsync<AccountDto>())!;

        // The master keeps its name and fills the details it was missing.
        (merged.Id, merged.Name, merged.Website, merged.Phone, merged.Industry).ShouldBe((master.Id, "Umbrella Group", "umbrella.test", "+44 20 7946 0000", "Pharma"));

        (await s.Client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact!.Id}"))!.AccountId.ShouldBe(master.Id);
        (await s.Client.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal!.Id}"))!.AccountId.ShouldBe(master.Id);
        (await s.Client.GetFromJsonAsync<TicketDto>($"/api/v1/tickets/{ticket!.Id}"))!.AccountId.ShouldBe(master.Id);
        (await s.Client.GetFromJsonAsync<AccountDto>($"/api/v1/accounts/{child.Id}"))!.ParentAccountId.ShouldBe(master.Id);

        // The duplicate is gone, and the master is no longer flagged as having duplicates.
        (await s.Client.GetAsync($"/api/v1/accounts/{dup.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Client.GetFromJsonAsync<List<DuplicateCandidateDto>>($"/api/v1/accounts/{master.Id}/duplicates"))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_merge_that_is_not_between_duplicates_or_is_badly_asked_is_refused_and_changes_nothing()
    {
        var s = await Workspaces.SignupAsync(factory);
        var a = await NewAccountAsync(s, "Stark Industries");
        var unrelated = await NewAccountAsync(s, "Wayne Enterprises");
        var twin = await NewAccountAsync(s, "Stark Industries LLC");

        (await MergeAsync(s, a.Id, unrelated.Id)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MergeAsync(s, a.Id, a.Id)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MergeAsync(s, a.Id)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MergeAsync(s, a.Id, twin.Id, twin.Id)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MergeAsync(s, a.Id, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MergeAsync(s, Guid.NewGuid(), twin.Id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await s.Client.GetAsync($"/api/v1/accounts/{unrelated.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Client.GetAsync($"/api/v1/accounts/{twin.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task When_the_master_is_a_child_of_a_duplicate_it_becomes_a_top_level_account()
    {
        var s = await Workspaces.SignupAsync(factory);
        var dup = await NewAccountAsync(s, "Hooli");
        var master = await NewAccountAsync(s, "Hooli Inc", parent: dup.Id);

        (await MergeAsync(s, master.Id, dup.Id)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Client.GetFromJsonAsync<AccountDto>($"/api/v1/accounts/{master.Id}"))!.ParentAccountId.ShouldBeNull();
    }

    [Fact]
    public async Task Another_company_cannot_see_check_or_merge_my_accounts_and_nobody_signed_out_can_either()
    {
        var mine = await Workspaces.SignupAsync(factory);
        var a = await NewAccountAsync(mine, "Pied Piper");
        var b = await NewAccountAsync(mine, "Pied Piper Inc");
        var other = await Workspaces.SignupAsync(factory);

        (await CheckAsync(other, "Pied Piper")).ShouldBeEmpty();
        (await other.Client.GetAsync($"/api/v1/accounts/{a.Id}/duplicates")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MergeAsync(other, a.Id, b.Id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/accounts/check-duplicates", new CheckAccountDuplicatesRequest("x", null, null))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/v1/accounts/{a.Id}/merge", new MergeAccountsRequest([b.Id]))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await mine.Client.GetAsync($"/api/v1/accounts/{b.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
