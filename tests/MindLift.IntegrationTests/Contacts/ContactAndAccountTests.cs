using System.Net;
using System.Net.Http.Json;
using System.Text;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Imports;
using Shouldly;

namespace MindLift.IntegrationTests.Contacts;

/// <summary>Contacts, accounts, the converted-lead history, hierarchy rules, import/export and the search box.</summary>
public sealed class ContactAndAccountTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static ListQuery All(string? search = null) => new(search, null, "first_name", "asc", 1, 200);

    private static SaveContactRequest NewContact(string first, string? email = null, Guid? accountId = null, string? type = null)
        => new(first, "Tester", email, null, "Buyer", null, accountId, type, null, null);

    private static SaveAccountRequest NewAccount(string name, Guid? parent = null)
        => new(name, "https://example.test", "+971 4 000 0000", "Retail", null, parent, null, null);

    private static async Task<T> PostAsync<T>(Session s, string url, object body)
    {
        var response = await s.Client.PostAsJsonAsync(url, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task A_contact_can_be_created_read_edited_and_deleted()
    {
        var s = await Workspaces.SignupAsync(factory);
        var account = await PostAsync<AccountDto>(s, "/api/v1/accounts", NewAccount("Acme Retail"));

        var created = await PostAsync<ContactDto>(s, "/api/v1/contacts", NewContact("Nadia", "Nadia@Example.test", account.Id, "client"));
        created.AccountName.ShouldBe("Acme Retail");

        var put = await s.Client.PutAsJsonAsync($"/api/v1/contacts/{created.Id}", NewContact("Nadia2", "nadia@example.test", account.Id, "client"));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{created.Id}"))!.FirstName.ShouldBe("Nadia2");

        (await s.Client.DeleteAsync($"/api/v1/contacts/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await s.Client.GetAsync($"/api/v1/contacts/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_contact_needs_a_first_name_a_real_type_and_a_real_account()
    {
        var s = await Workspaces.SignupAsync(factory);

        (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("A", type: "nonsense"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("A", accountId: Guid.NewGuid()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact(new string('x', 101)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_list_searches_sorts_and_pages()
    {
        var s = await Workspaces.SignupAsync(factory);
        await PostAsync<ContactDto>(s, "/api/v1/contacts", NewContact("Alice", "alice@example.test"));
        await PostAsync<ContactDto>(s, "/api/v1/contacts", NewContact("Bob", "bob@example.test"));

        var found = await PostAsync<PagedResult<ContactListItemDto>>(s, "/api/v1/contacts/query", All("bob"));
        found.Items.Select(i => i.FirstName).ShouldBe(["Bob"]);

        var paged = await PostAsync<PagedResult<ContactListItemDto>>(s, "/api/v1/contacts/query", new ListQuery(null, null, "first_name", "asc", 2, 1));
        paged.Total.ShouldBe(2);
        paged.Items.Single().FirstName.ShouldBe("Bob");
    }

    [Fact]
    public async Task Duplicates_are_found_by_email_and_merging_keeps_the_master_and_moves_the_missing_details()
    {
        var s = await Workspaces.SignupAsync(factory);
        var master = await PostAsync<ContactDto>(s, "/api/v1/contacts", NewContact("Ivy", "ivy@example.test"));
        var duplicate = await PostAsync<ContactDto>(s, "/api/v1/contacts", new SaveContactRequest("Ivy", "Tester", "IVY@example.test", "+971501234567", "CFO", null, null, null, null, null));

        var candidates = await PostAsync<List<DuplicateCandidateDto>>(s, "/api/v1/contacts/check-duplicates", new CheckDuplicatesRequest("Ivy", "Tester", "ivy@example.test", null, null));
        candidates.Count.ShouldBe(2);

        var merged = await PostAsync<ContactDto>(s, $"/api/v1/contacts/{master.Id}/merge", new MergeContactsRequest([duplicate.Id]));
        merged.Phone.ShouldBe("+971501234567");
        (await s.Client.GetAsync($"/api/v1/contacts/{duplicate.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_converted_lead_shows_its_history_on_the_contact()
    {
        var s = await Workspaces.SignupAsync(factory);
        var lead = await PostAsync<LeadDto>(s, "/api/v1/leads", new SaveLeadRequest("Conv", "Lead", "conv@example.test", null, "Conv Co", "new", "website", null, null, null, null, null));
        var result = await PostAsync<ConvertLeadResult>(s, $"/api/v1/leads/{lead.Id}/convert", new ConvertLeadRequest("create", null, null, "new", null, false, null, null, null, null, null, null));

        var summary = await s.Client.GetFromJsonAsync<ContactSummaryDto>($"/api/v1/contacts/{result.ContactId}/summary");

        summary!.ConvertedFrom.ShouldNotBeNull();
        summary.ConvertedFrom!.LeadId.ShouldBe(lead.Id);
        summary.ConvertedFrom.Source.ShouldBe("website");
        summary.Contact.AccountName.ShouldBe("Conv Co");
    }

    [Fact]
    public async Task An_account_page_lists_its_contacts_and_child_accounts()
    {
        var s = await Workspaces.SignupAsync(factory);
        var parent = await PostAsync<AccountDto>(s, "/api/v1/accounts", NewAccount("Parent Group"));
        var child = await PostAsync<AccountDto>(s, "/api/v1/accounts", NewAccount("Child Branch", parent.Id));
        await PostAsync<ContactDto>(s, "/api/v1/contacts", NewContact("Pat", "pat@example.test", parent.Id));

        var summary = await s.Client.GetFromJsonAsync<AccountSummaryDto>($"/api/v1/accounts/{parent.Id}/summary");

        summary!.Contacts.Select(c => c.FullName).ShouldBe(["Pat Tester"]);
        summary.Children.Select(c => c.Id).ShouldBe([child.Id]);
        (await s.Client.GetFromJsonAsync<AccountDto>($"/api/v1/accounts/{child.Id}"))!.ParentName.ShouldBe("Parent Group");
    }

    [Fact]
    public async Task Account_names_are_unique_and_the_hierarchy_cannot_loop()
    {
        var s = await Workspaces.SignupAsync(factory);
        var a = await PostAsync<AccountDto>(s, "/api/v1/accounts", NewAccount("Alpha"));
        var b = await PostAsync<AccountDto>(s, "/api/v1/accounts", NewAccount("Beta", a.Id));

        (await s.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("alpha"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Client.PutAsJsonAsync($"/api/v1/accounts/{a.Id}", NewAccount("Alpha", a.Id))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Client.PutAsJsonAsync($"/api/v1/accounts/{a.Id}", NewAccount("Alpha", b.Id))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deleting_an_account_keeps_its_contacts_but_is_refused_while_it_has_children()
    {
        var s = await Workspaces.SignupAsync(factory);
        var parent = await PostAsync<AccountDto>(s, "/api/v1/accounts", NewAccount("Parent"));
        var child = await PostAsync<AccountDto>(s, "/api/v1/accounts", NewAccount("Child", parent.Id));
        var contact = await PostAsync<ContactDto>(s, "/api/v1/contacts", NewContact("Kept", accountId: child.Id));

        (await s.Client.DeleteAsync($"/api/v1/accounts/{parent.Id}")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await s.Client.DeleteAsync($"/api/v1/accounts/{child.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await s.Client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}"))!.AccountId.ShouldBeNull();
    }

    [Fact]
    public async Task Contacts_import_skips_duplicates_links_accounts_and_export_back_to_csv()
    {
        var s = await Workspaces.SignupAsync(factory);
        await PostAsync<AccountDto>(s, "/api/v1/accounts", NewAccount("Linked Co"));

        var csv = "First name,Last name,Email,Company\nAmal,One,amal@example.test,Linked Co\nAmal,Dup,amal@example.test,\n,NoFirst,x@example.test,\n";
        var response = await s.Client.PostAsync("/api/v1/contacts/import", new StringContent(csv, Encoding.UTF8, "text/csv"));
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        (result.Created, result.Skipped, result.Failed).ShouldBe((1, 1, 1));

        var export = await s.Client.PostAsJsonAsync("/api/v1/contacts/export", All());
        export.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var rows = CsvText.Parse((await export.Content.ReadAsStringAsync()).TrimStart('﻿'));
        rows[0].ShouldBe(["id", "first_name", "last_name", "email", "phone", "job_title", "account", "type", "owner", "created_at"]);
        rows[1][rows[0].IndexOf("account")].ShouldBe("Linked Co");
    }

    [Fact]
    public async Task Accounts_import_skips_names_that_exist_and_export_includes_the_contact_count()
    {
        var s = await Workspaces.SignupAsync(factory);
        var existing = await PostAsync<AccountDto>(s, "/api/v1/accounts", NewAccount("Existing"));
        await PostAsync<ContactDto>(s, "/api/v1/contacts", NewContact("One", accountId: existing.Id));

        var response = await s.Client.PostAsync("/api/v1/accounts/import", new StringContent("Name,Industry\nExisting,X\nFresh,Retail\n,Nothing\n", Encoding.UTF8, "text/csv"));
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        (result.Created, result.Skipped, result.Failed).ShouldBe((1, 1, 1));

        var rows = CsvText.Parse((await (await s.Client.PostAsJsonAsync("/api/v1/accounts/export", All())).Content.ReadAsStringAsync()).TrimStart('﻿'));
        rows.Single(r => r[1] == "Existing")[rows[0].IndexOf("contacts")].ShouldBe("1");
    }

    [Fact]
    public async Task The_search_box_finds_contacts_accounts_and_leads_and_ignores_short_text()
    {
        var s = await Workspaces.SignupAsync(factory);
        await PostAsync<ContactDto>(s, "/api/v1/contacts", NewContact("Zebulon", "zeb@example.test"));
        await PostAsync<AccountDto>(s, "/api/v1/accounts", NewAccount("Zebra Trading"));
        await PostAsync<LeadDto>(s, "/api/v1/leads", new SaveLeadRequest("Zelda", "Lead", null, null, "Zed Co", "new", "website", null, null, null, null, null));

        var hits = (await s.Client.GetFromJsonAsync<List<SearchHitDto>>("/api/v1/search?q=ze"))!;
        hits.Select(h => h.Kind).Distinct().Order().ToList().ShouldBe(["account", "contact", "lead"]);

        (await s.Client.GetFromJsonAsync<List<SearchHitDto>>("/api/v1/search?q=z"))!.ShouldBeEmpty();
        (await s.Client.GetFromJsonAsync<List<SearchHitDto>>("/api/v1/search?q=nomatchatall"))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_workspace_cannot_see_another_workspaces_contacts_or_search_results()
    {
        var first = await Workspaces.SignupAsync(factory);
        var second = await Workspaces.SignupAsync(factory);
        var contact = await PostAsync<ContactDto>(first, "/api/v1/contacts", NewContact("Secretive", "secret@example.test"));

        (await second.Client.GetAsync($"/api/v1/contacts/{contact.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await second.Client.GetFromJsonAsync<List<SearchHitDto>>("/api/v1/search?q=secretive"))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_needs_a_signed_in_user()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/search?q=abc")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
