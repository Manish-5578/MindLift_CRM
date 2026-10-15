using System.Net.Http.Json;
using System.Text;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>Contacts, accounts and the search box used the way a person uses them.</summary>
[Collection(E2ECollection.Name)]
public sealed class ContactsAndAccountsE2ETests(E2EHost host)
{
    private async Task<(IPage Page, HttpClient Api)> StartAsync(int width = 1440)
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync(width);
        await E2EHost.LoginAsync(page, workspace);
        return (page, host.ApiFor(workspace));
    }

    private static async Task<AccountDto> CreateAccountAsync(HttpClient api, string name, Guid? parent = null)
    {
        var response = await api.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest(name, null, null, "Retail", null, parent, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountDto>())!;
    }

    private static async Task<ContactDto> CreateContactAsync(HttpClient api, string first, string? email = null, Guid? accountId = null)
    {
        var response = await api.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest(first, "Tester", email, null, "Buyer", null, accountId, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    private static ILocator Drawer(IPage page) => page.Locator("[data-testid=form-drawer].mud-drawer--open");

    [E2EFact]
    public async Task A_contact_can_be_created_with_a_company_picked_from_the_search_and_appears_in_the_list()
    {
        var (page, api) = await StartAsync();
        await CreateAccountAsync(api, "Acme Retail");
        await page.GotoAsync("/contacts");
        await Expect(page.Locator("[data-testid=entity-list]")).ToContainTextAsync("No contacts yet");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        var drawer = Drawer(page);
        await drawer.Locator("input[data-testid=contact-first]").FillAsync("Nadia");
        await drawer.GetByLabel("Last name").FillAsync("Hassan");
        await drawer.Locator("input[data-testid=contact-account]").FillAsync("acm");
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Acme Retail" }).ClickAsync();
        await drawer.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create contact" }).ClickAsync();

        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=row]")).ToContainTextAsync("Nadia Hassan");
        await Expect(page.Locator("[data-testid=row]")).ToContainTextAsync("Acme Retail");
        await Shots.SaveAsync(page, "contacts-list");
    }

    [E2EFact]
    public async Task A_contact_needs_a_first_name()
    {
        var (page, _) = await StartAsync();
        await page.GotoAsync("/contacts");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create contact" }).ClickAsync();

        await Expect(Drawer(page)).ToContainTextAsync("Enter the first name.");
    }

    [E2EFact]
    public async Task A_contact_can_be_opened_edited_and_deleted()
    {
        var (page, api) = await StartAsync();
        var contact = await CreateContactAsync(api, "Edit", "edit@example.test");
        await page.GotoAsync($"/contacts/{contact.Id}");
        await Expect(page.Locator("[data-testid=contact-details]")).ToContainTextAsync("edit@example.test");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).GetByLabel("Job title").FillAsync("Head of Sales");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save changes" }).ClickAsync();
        await Expect(page.Locator("[data-testid=contact-details]")).ToContainTextAsync("Head of Sales");
        await Shots.SaveAsync(page, "contact-detail");

        await page.Locator("[data-testid=delete-contact]").ClickAsync();
        await page.Locator("[data-testid=confirm-ok]").ClickAsync();
        await page.WaitForURLAsync("**/contacts");
        await Expect(page.Locator("[data-testid=entity-list]")).ToContainTextAsync("No contacts yet");
    }

    [E2EFact]
    public async Task A_converted_lead_shows_its_history_on_the_new_contact()
    {
        var (page, api) = await StartAsync();
        var lead = await (await api.PostAsJsonAsync("/api/v1/leads",
            new SaveLeadRequest("Hist", "Lead", "hist@example.test", null, "Hist Co", "new", "website", null, null, null, null, null))).Content.ReadFromJsonAsync<LeadDto>();
        var result = await (await api.PostAsJsonAsync($"/api/v1/leads/{lead!.Id}/convert",
            new ConvertLeadRequest("create", null, null, "new", null, false, null, null, null, null, null, null))).Content.ReadFromJsonAsync<ConvertLeadResult>();

        await page.GotoAsync($"/contacts/{result!.ContactId}");

        await Expect(page.Locator("[data-testid=record-timeline]")).ToContainTextAsync("Converted from lead Hist Lead");
        await Expect(page.Locator("[data-testid=contact-account-link]")).ToHaveTextAsync("Hist Co");
    }

    [E2EFact]
    public async Task Duplicate_contacts_are_found_and_merged()
    {
        var (page, api) = await StartAsync();
        var master = await CreateContactAsync(api, "Ivy", "ivy@example.test");
        await CreateContactAsync(api, "Ivy", "IVY@example.test");
        await page.GotoAsync($"/contacts/{master.Id}");

        await page.Locator("[data-testid=find-duplicates]").ClickAsync();
        await Expect(page.Locator("[data-testid=duplicate-row]")).ToHaveCountAsync(1);
        await page.GetByLabel("Merge Ivy Tester").CheckAsync();
        await page.Locator("[data-testid=merge-submit]").ClickAsync();
        await page.Locator("[data-testid=confirm-ok]").ClickAsync();

        await Expect(page.GetByText("Merged 1 contact")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task An_account_page_lists_its_contacts_and_a_contact_can_be_added_from_it()
    {
        var (page, api) = await StartAsync();
        var account = await CreateAccountAsync(api, "Parent Group");
        await CreateContactAsync(api, "Existing", "existing@example.test", account.Id);
        var child = await CreateAccountAsync(api, "Child Branch", account.Id);
        await page.GotoAsync($"/accounts/{account.Id}");
        await Expect(page.Locator("[data-testid=account-contact]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=child-link]")).ToHaveTextAsync("Child Branch");

        await page.Locator("[data-testid=add-contact]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=contact-first]").FillAsync("Added");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create contact" }).ClickAsync();

        await Expect(page.Locator("[data-testid=account-contact]")).ToHaveCountAsync(2);
        await Shots.SaveAsync(page, "account-detail");

        await page.Locator("[data-testid=child-link]").ClickAsync();
        await page.WaitForURLAsync($"**/accounts/{child.Id}");
        await Expect(page.Locator("[data-testid=parent-link]")).ToContainTextAsync("Parent Group");
    }

    [E2EFact]
    public async Task An_account_cannot_be_its_own_ancestor()
    {
        var (page, api) = await StartAsync();
        var parent = await CreateAccountAsync(api, "Top");
        await CreateAccountAsync(api, "Under", parent.Id);
        await page.GotoAsync($"/accounts/{parent.Id}");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=account-parent]").FillAsync("Und");
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Under" }).ClickAsync();
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save changes" }).ClickAsync();

        await Expect(Drawer(page)).ToContainTextAsync("loop");
    }

    [E2EFact]
    public async Task Deleting_an_account_that_has_children_is_refused_with_a_reason()
    {
        var (page, api) = await StartAsync();
        var parent = await CreateAccountAsync(api, "Has Kids");
        await CreateAccountAsync(api, "Kid", parent.Id);
        await page.GotoAsync($"/accounts/{parent.Id}");

        await page.Locator("[data-testid=delete-account]").ClickAsync();
        await page.Locator("[data-testid=confirm-ok]").ClickAsync();

        await Expect(page.GetByText("parent of other accounts")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task Contacts_can_be_imported_from_a_csv_file_and_linked_to_existing_companies()
    {
        var (page, api) = await StartAsync();
        await CreateAccountAsync(api, "Linked Co");
        await page.GotoAsync("/contacts/import");

        await page.Locator("[data-testid=file-dropzone] input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "contacts.csv",
            MimeType = "text/csv",
            Buffer = Encoding.UTF8.GetBytes("First name,Last name,Email,Company\nAmal,One,amal@example.test,Linked Co\nBo,Two,bo@example.test,\n"),
        });
        await Expect(page.Locator("[data-testid=import-step-check]")).ToContainTextAsync("2 rows found");
        await page.Locator("[data-testid=import-submit]").ClickAsync();
        await Expect(page.Locator("[data-testid=import-step-result]")).ToContainTextAsync("2 imported");
        await page.Locator("[data-testid=import-done]").ClickAsync();

        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(2);
        await Expect(page.Locator("[data-testid=entity-list]")).ToContainTextAsync("Linked Co");
    }


    [E2EFact]
    public async Task A_latin1_csv_file_is_imported_with_its_accented_letters_intact()
    {
        var (page, _) = await StartAsync();
        await page.GotoAsync("/contacts/import");

        await page.Locator("[data-testid=file-dropzone] input[type=file]").SetInputFilesAsync(new FilePayload
        {
            Name = "latin1.csv",
            MimeType = "text/csv",
            Buffer = Encoding.Latin1.GetBytes("First name,Last name\nJosé,Müller\n"),
        });
        await Expect(page.Locator("[data-testid=import-preview]")).ToContainTextAsync("José");
        await page.Locator("[data-testid=import-submit]").ClickAsync();
        await Expect(page.Locator("[data-testid=import-step-result]")).ToContainTextAsync("1 imported");
        await page.Locator("[data-testid=import-done]").ClickAsync();

        await Expect(page.Locator("[data-testid=row]")).ToContainTextAsync("José Müller");
    }
    [E2EFact]
    public async Task Accounts_can_be_exported_to_a_csv_file()
    {
        var (page, api) = await StartAsync();
        await CreateAccountAsync(api, "Export Me");
        await page.GotoAsync("/accounts");
        await Expect(page.Locator("[data-testid=row]")).ToHaveCountAsync(1);

        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("[data-testid=export-accounts]").ClickAsync());

        Assert.Equal("accounts.csv", download.SuggestedFilename);
        Assert.Contains("Export Me", await File.ReadAllTextAsync((await download.PathAsync())!));
    }

    [E2EFact]
    public async Task The_search_box_finds_records_of_every_kind_and_opens_the_one_chosen()
    {
        var (page, api) = await StartAsync();
        await CreateAccountAsync(api, "Zebra Trading");
        var contact = await CreateContactAsync(api, "Zebulon", "zeb@example.test");
        await page.GotoAsync("/");

        await page.Locator("input[data-testid=global-search]").FillAsync("zeb");
        await Expect(page.Locator("[data-testid=search-hit]")).ToHaveCountAsync(2);

        await page.Locator("[data-testid=search-hit]").Filter(new LocatorFilterOptions { HasText = "Zebulon" }).ClickAsync();
        await page.WaitForURLAsync($"**/contacts/{contact.Id}");
        await Expect(page.Locator("[data-testid=page-title]")).ToHaveTextAsync("Zebulon Tester");
    }

    [E2EFact]
    public async Task The_search_box_says_when_nothing_matches()
    {
        var (page, _) = await StartAsync();
        await page.GotoAsync("/");

        await page.Locator("input[data-testid=global-search]").FillAsync("nomatchatall");

        await Expect(page.Locator("[data-testid=search-empty]")).ToBeVisibleAsync();
    }
}
