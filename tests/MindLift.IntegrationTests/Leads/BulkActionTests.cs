using System.Net;
using System.Net.Http.Json;
using System.Text;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Imports;
using Shouldly;

namespace MindLift.IntegrationTests.Leads;

/// <summary>
/// Bulk actions from the list screens (plan 18.8.1): many records assigned, re-statused, tagged or deleted at once.
/// Each record goes through the same checks as editing it alone, a record that cannot be changed is reported, and nothing else about a record changes.
/// </summary>
public sealed class BulkActionTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task<Guid> IdAsync(Session session)
        => (await session.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.UserId;

    private static async Task<LeadDto> LeadAsync(Session session, string first, string? company = null, IReadOnlyList<string>? tags = null, string? description = null)
    {
        var response = await session.Client.PostAsJsonAsync("/api/v1/leads",
            new SaveLeadRequest(first, "Bulk", $"{first.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test", null, company, "new", "website", "warm", description, tags, null, null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LeadDto>())!;
    }

    private static async Task<LeadDto> GetLeadAsync(Session session, Guid id)
        => (await session.Client.GetFromJsonAsync<LeadDto>($"/api/v1/leads/{id}"))!;

    private static async Task<BulkActionResult> BulkAsync(Session session, string entity, object request)
    {
        var response = await session.Client.PostAsJsonAsync($"/api/v1/{entity}/bulk", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BulkActionResult>())!;
    }

    private static async Task<BulkActionResult> BulkDeleteAsync(Session session, string entity, BulkDeleteRequest request)
    {
        var response = await session.Client.PostAsJsonAsync($"/api/v1/{entity}/bulk-delete", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BulkActionResult>())!;
    }

    [Fact]
    public async Task Ticked_leads_are_assigned_and_nothing_else_about_them_changes()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "bulk-assign");
        var staffId = await IdAsync(staff);
        var first = await LeadAsync(admin, "Asha", "Acme", ["vip"], "Met at the expo");
        var second = await LeadAsync(admin, "Bilal", "Globex");
        var untouched = await LeadAsync(admin, "Chen");

        var result = await BulkAsync(admin, "leads", new BulkActionRequest(BulkActions.Assign, [first.Id, second.Id], null, OwnerId: staffId));

        result.Requested.ShouldBe(2);
        result.Succeeded.ShouldBe(2);
        result.Failures.ShouldBeEmpty();

        var after = await GetLeadAsync(admin, first.Id);
        after.OwnerId.ShouldBe(staffId);
        after.CompanyName.ShouldBe("Acme");
        after.Tags.ShouldBe(["vip"]);
        after.Description.ShouldBe("Met at the expo");
        after.Status.ShouldBe("new");
        (await GetLeadAsync(admin, second.Id)).OwnerId.ShouldBe(staffId);
        (await GetLeadAsync(admin, untouched.Id)).OwnerId.ShouldBe(first.OwnerId);
    }

    [Fact]
    public async Task Select_all_changes_every_lead_the_list_matches_and_no_others()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var acmeOne = await LeadAsync(admin, "Dana", "Acme Trading");
        var acmeTwo = await LeadAsync(admin, "Eli", "Acme Trading");
        var other = await LeadAsync(admin, "Fay", "Initech");

        var result = await BulkAsync(admin, "leads",
            new BulkActionRequest(BulkActions.ChangeStatus, null, new ListQuery("acme", null, null, null), Status: "working"));

        result.Succeeded.ShouldBe(2);
        (await GetLeadAsync(admin, acmeOne.Id)).Status.ShouldBe("working");
        (await GetLeadAsync(admin, acmeTwo.Id)).Status.ShouldBe("working");
        (await GetLeadAsync(admin, other.Id)).Status.ShouldBe("new");
    }

    [Fact]
    public async Task Tags_are_added_to_the_ones_a_lead_already_has()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var lead = await LeadAsync(admin, "Gia", tags: ["expo"]);

        await BulkAsync(admin, "leads", new BulkActionRequest(BulkActions.AddTags, [lead.Id], null, Tags: ["q4", "expo"]));

        (await GetLeadAsync(admin, lead.Id)).Tags.Order().ShouldBe(["expo", "q4"]);
    }

    [Fact]
    public async Task Leads_cannot_be_disqualified_in_bulk_because_each_needs_a_reason()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var lead = await LeadAsync(admin, "Hana");

        var response = await admin.Client.PostAsJsonAsync("/api/v1/leads/bulk", new BulkActionRequest(BulkActions.ChangeStatus, [lead.Id], null, Status: "disqualified"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetLeadAsync(admin, lead.Id)).Status.ShouldBe("new");
    }

    [Fact]
    public async Task A_lead_that_cannot_be_changed_is_reported_and_the_rest_still_go_ahead()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var converted = await LeadAsync(admin, "Ivan", "Umbrella");
        var convert = await admin.Client.PostAsJsonAsync($"/api/v1/leads/{converted.Id}/convert",
            new ConvertLeadRequest("create", null, null, "new", null, false, null, null, null, null, null, null));
        convert.StatusCode.ShouldBe(HttpStatusCode.OK, await convert.Content.ReadAsStringAsync());
        var plain = await LeadAsync(admin, "Jade");

        var result = await BulkDeleteAsync(admin, "leads", new BulkDeleteRequest([converted.Id, plain.Id], null));

        result.Succeeded.ShouldBe(1);
        result.Failures.Single().Id.ShouldBe(converted.Id);
        result.Failures.Single().Message.ShouldContain("Converted");
        (await admin.Client.GetAsync($"/api/v1/leads/{plain.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Records_a_user_cannot_see_are_reported_not_changed()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "bulk-scope");
        var adminsLead = await LeadAsync(admin, "Kai");

        var result = await BulkAsync(staff, "leads", new BulkActionRequest(BulkActions.AddTags, [adminsLead.Id], null, Tags: ["mine"]));

        result.Succeeded.ShouldBe(0);
        result.Failures.Single().Id.ShouldBe(adminsLead.Id);
        (await GetLeadAsync(admin, adminsLead.Id)).Tags.ShouldBeEmpty();
    }

    [Fact]
    public async Task Assigning_to_someone_outside_the_workspace_changes_nothing()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var lead = await LeadAsync(admin, "Lior");

        var result = await BulkAsync(admin, "leads", new BulkActionRequest(BulkActions.Assign, [lead.Id], null, OwnerId: Guid.NewGuid()));

        result.Succeeded.ShouldBe(0);
        (await GetLeadAsync(admin, lead.Id)).OwnerId.ShouldBe(lead.OwnerId);
    }

    [Fact]
    public async Task Select_all_over_the_limit_is_refused_so_the_user_narrows_the_list()
    {
        var admin = await Workspaces.SignupAsync(factory);
        admin.Client.Timeout = TimeSpan.FromMinutes(10);
        var csv = new StringBuilder("First name,Last name,Email\n");
        for (var i = 0; i <= BulkActions.MaxRecords; i++)
        {
            csv.Append($"Many{i:D3},Test,many{i:D3}-{Guid.NewGuid():N}@example.test\n");
        }

        var import = await admin.Client.PostAsync("/api/v1/leads/import", new StringContent(csv.ToString(), Encoding.UTF8, "text/csv"));
        (await import.Content.ReadFromJsonAsync<ImportResultDto>())!.Created.ShouldBe(BulkActions.MaxRecords + 1);

        var response = await admin.Client.PostAsJsonAsync("/api/v1/leads/bulk-delete", new BulkDeleteRequest(null, new ListQuery(null, null, null, null)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var left = await (await admin.Client.PostAsJsonAsync("/api/v1/leads/query", new ListQuery(null, null, null, null, 1, 1)))
            .Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        left!.Total.ShouldBe(BulkActions.MaxRecords + 1);
    }

    [Fact]
    public async Task An_export_of_ticked_rows_contains_only_those_rows()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var picked = await LeadAsync(admin, "Mira");
        await LeadAsync(admin, "Noor");

        var response = await admin.Client.PostAsJsonAsync("/api/v1/leads/export", new ListQuery(null, null, null, null, Ids: [picked.Id]));
        var rows = CsvText.Parse((await response.Content.ReadAsStringAsync()).TrimStart('﻿'));

        rows.Count.ShouldBe(2);
        rows[1].ShouldContain(picked.Id.ToString());
    }

    [Fact]
    public async Task Contacts_accounts_and_deals_can_be_assigned_and_deleted_in_bulk()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "bulk-records");
        var staffId = await IdAsync(staff);

        var account = await (await admin.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Bulk Co", "bulk.test", null, "Retail", "Keep me", null, null, null)))
            .Content.ReadFromJsonAsync<AccountDto>();
        var contact = await (await admin.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Omar", "Bulk", $"omar-{Guid.NewGuid():N}@example.test", null, "Buyer", null, account!.Id, null, null, null)))
            .Content.ReadFromJsonAsync<ContactDto>();
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        var pipeline = (await admin.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!.First(p => p.IsDefault);
        var dealResponse = await admin.Client.PostAsJsonAsync("/api/v1/deals",
            new SaveDealRequest("Bulk deal", pipeline.Id, null, 1200, null, null, null, account.Id, [new DealContactInput(contact!.Id, "Buyer", true)], null, null, null));
        dealResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await dealResponse.Content.ReadAsStringAsync());
        var deal = (await dealResponse.Content.ReadFromJsonAsync<DealDto>())!;

        (await BulkAsync(admin, "accounts", new BulkActionRequest(BulkActions.Assign, [account.Id], null, OwnerId: staffId))).Succeeded.ShouldBe(1);
        (await BulkAsync(admin, "contacts", new BulkActionRequest(BulkActions.Assign, [contact.Id], null, OwnerId: staffId))).Succeeded.ShouldBe(1);
        (await BulkAsync(admin, "deals", new BulkActionRequest(BulkActions.Assign, [deal.Id], null, OwnerId: staffId))).Succeeded.ShouldBe(1);

        var accountAfter = (await admin.Client.GetFromJsonAsync<AccountDto>($"/api/v1/accounts/{account.Id}"))!;
        accountAfter.OwnerId.ShouldBe(staffId);
        accountAfter.Description.ShouldBe("Keep me");
        (await admin.Client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}"))!.AccountId.ShouldBe(account.Id);
        var dealAfter = (await admin.Client.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal.Id}"))!;
        dealAfter.OwnerId.ShouldBe(staffId);
        dealAfter.Amount.ShouldBe(1200);
        dealAfter.Contacts.Single().ContactId.ShouldBe(contact.Id);

        (await BulkDeleteAsync(admin, "deals", new BulkDeleteRequest([deal.Id], null))).Succeeded.ShouldBe(1);
        (await admin.Client.GetAsync($"/api/v1/deals/{deal.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bulk_delete_needs_the_same_permission_as_deleting_one_record()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "bulk-perm");
        var own = await LeadAsync(staff, "Pia");

        // Staff can edit their own leads but not delete any.
        (await staff.Client.PostAsJsonAsync("/api/v1/leads/bulk-delete", new BulkDeleteRequest([own.Id], null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.Client.GetAsync($"/api/v1/leads/{own.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_single_edit_cannot_give_a_record_to_someone_outside_the_workspace()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var stranger = await Workspaces.SignupAsync(factory, "Another company");
        var lead = await LeadAsync(admin, "Quinn");

        var response = await admin.Client.PutAsJsonAsync($"/api/v1/leads/{lead.Id}",
            new SaveLeadRequest(lead.FirstName, lead.LastName, lead.Email, null, null, "new", "website", "warm", null, null, await IdAsync(stranger), null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetLeadAsync(admin, lead.Id)).OwnerId.ShouldBe(lead.OwnerId);
    }
}
