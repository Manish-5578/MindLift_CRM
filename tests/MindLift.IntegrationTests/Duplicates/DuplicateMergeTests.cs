using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Duplicates;

/// <summary>
/// Phase 1 / #3: Duplicate Detection and Merge (duplicate-merge).
/// Verifies duplicate candidate discovery, side-by-side field overrides,
/// relationship and child re-parenting (activities, deals, accounts, timeline),
/// 3-record merge limit, permission enforcement, and multi-tenant isolation.
/// </summary>
public sealed class DuplicateMergeTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s, string feature)
        => (await s.Client.PutAsJsonAsync($"/api/v1/features/{feature}", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    private static SaveLeadRequest NewLead(string first, string last, string email, string? phone = null, string? company = null)
        => new(first, last, email, phone, company, "new", "website", null, null, null, null, null);

    private static SaveContactRequest NewContact(string first, string last, string email, string? phone = null, string? title = null, Guid? accountId = null)
        => new(first, last, email, phone, title, null, accountId, null, null, null);

    [Fact]
    public async Task Feature_flag_off_blocks_advanced_endpoints_and_overrides_while_standard_merge_works()
    {
        var s = await Workspaces.SignupAsync(factory);

        var lead1 = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Jane", "Doe", "jane@example.test", "+1112223333"))).Content.ReadFromJsonAsync<LeadDto>();
        var lead2 = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Jane", "Doe", "jane@example.test", "+1112224444"))).Content.ReadFromJsonAsync<LeadDto>();

        // GET /duplicates blocked by RequiresFeature (answers 404 to hide disabled feature)
        var getDupRes = await s.Client.GetAsync($"/api/v1/leads/{lead1!.Id}/duplicates");
        getDupRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Side-by-side merge with FieldOverrides blocked by feature flag (answers 403 Forbidden)
        var sideBySideRes = await s.Client.PostAsJsonAsync($"/api/v1/leads/{lead1.Id}/merge", new MergeLeadsRequest(
            [lead2!.Id],
            new Dictionary<string, string?> { ["phone"] = "+1112224444" }
        ));
        sideBySideRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Standard merge without FieldOverrides succeeds even with flag OFF
        var stdMergeRes = await s.Client.PostAsJsonAsync($"/api/v1/leads/{lead1.Id}/merge", new MergeLeadsRequest([lead2.Id]));
        stdMergeRes.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Feature_flag_on_allows_finding_duplicates_for_existing_lead_and_contact()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "duplicate-merge");

        var lead1 = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Charlie", "Brown", "charlie@peanuts.test", "+971501112233", "Peanuts Inc"))).Content.ReadFromJsonAsync<LeadDto>();
        var lead2 = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Charlie", "Brown", "charlie.other@peanuts.test", "+971501112233", "Peanuts Inc"))).Content.ReadFromJsonAsync<LeadDto>();

        var duplicates = await s.Client.GetFromJsonAsync<List<DuplicateCandidateDto>>($"/api/v1/leads/{lead1!.Id}/duplicates");
        duplicates.ShouldNotBeNull();
        duplicates.Count.ShouldBeGreaterThanOrEqualTo(1);
        duplicates.ShouldContain(d => d.Id == lead2!.Id);
        duplicates.ShouldNotContain(d => d.Id == lead1.Id);

        var contact1 = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Lucy", "VanPelt", "lucy@peanuts.test", "+971509998877"))).Content.ReadFromJsonAsync<ContactDto>();
        var contact2 = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Lucy", "VanPelt", "lucy.personal@peanuts.test", "+971509998877"))).Content.ReadFromJsonAsync<ContactDto>();

        var contactDups = await s.Client.GetFromJsonAsync<List<DuplicateCandidateDto>>($"/api/v1/contacts/{contact1!.Id}/duplicates");
        contactDups.ShouldNotBeNull();
        contactDups.Count.ShouldBeGreaterThanOrEqualTo(1);
        contactDups.ShouldContain(d => d.Id == contact2!.Id);
        contactDups.ShouldNotContain(d => d.Id == contact1.Id);
    }

    [Fact]
    public async Task Side_by_side_merge_on_lead_applies_field_overrides_and_reparents_activities()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "duplicate-merge");

        var master = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Alice", "Wonder", "alice@wonder.test", null, "Acme Original"))).Content.ReadFromJsonAsync<LeadDto>();
        var dup1 = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Alice", "Wonder", "alice@wonder.test", "+971501112222", "Acme Branch"))).Content.ReadFromJsonAsync<LeadDto>();
        var dup2 = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Alice", "Wonder", "alice@wonder.test", "+971503334444", "Acme Global"))).Content.ReadFromJsonAsync<LeadDto>();

        // Create an activity linked to dup1
        var actRes = await s.Client.PostAsJsonAsync("/api/v1/activities", new SaveActivityRequest(
            "Call",
            "Initial discovery call with duplicate lead",
            null,
            null,
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null,
            null,
            dup1!.Id,
            null,
            null,
            null,
            null
        ));
        actRes.EnsureSuccessStatusCode();
        var activity = await actRes.Content.ReadFromJsonAsync<ActivityDto>();

        // Merge up to 3 records total (master + 2 duplicates) with explicit field overrides
        var mergeRes = await s.Client.PostAsJsonAsync($"/api/v1/leads/{master!.Id}/merge", new MergeLeadsRequest(
            [dup1.Id, dup2!.Id],
            new Dictionary<string, string?>
            {
                ["company_name"] = "Acme Global Enterprise",
                ["phone"] = "+971501112222",
                ["description"] = "Consolidated from side-by-side merge"
            }
        ));
        mergeRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var merged = await mergeRes.Content.ReadFromJsonAsync<LeadDto>();

        merged!.CompanyName.ShouldBe("Acme Global Enterprise");
        merged.Phone.ShouldBe("+971501112222");
        merged.Description.ShouldBe("Consolidated from side-by-side merge");

        // Duplicates soft deleted
        (await s.Client.GetAsync($"/api/v1/leads/{dup1.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Client.GetAsync($"/api/v1/leads/{dup2.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Activity re-parented to master lead (no orphans)
        var reparentedAct = await s.Client.GetFromJsonAsync<ActivityDto>($"/api/v1/activities/{activity!.Id}");
        reparentedAct!.LeadId.ShouldBe(master.Id);
    }

    [Fact]
    public async Task Side_by_side_merge_enforces_maximum_of_three_records_total()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "duplicate-merge");

        var master = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Dan", "Smith", "dan@example.test"))).Content.ReadFromJsonAsync<LeadDto>();
        var dup1 = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Dan", "Smith", "dan@example.test"))).Content.ReadFromJsonAsync<LeadDto>();
        var dup2 = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Dan", "Smith", "dan@example.test"))).Content.ReadFromJsonAsync<LeadDto>();
        var dup3 = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Dan", "Smith", "dan@example.test"))).Content.ReadFromJsonAsync<LeadDto>();

        // Attempting to merge 3 duplicates (4 records total) with FieldOverrides must fail
        var mergeRes = await s.Client.PostAsJsonAsync($"/api/v1/leads/{master!.Id}/merge", new MergeLeadsRequest(
            [dup1!.Id, dup2!.Id, dup3!.Id],
            new Dictionary<string, string?> { ["company_name"] = "Too many" }
        ));
        mergeRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Contact_merge_reparents_deals_and_accounts_cleanly_without_collision()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "duplicate-merge");
        await EnableFeatureAsync(s, "contact-multi-account");

        (await s.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();

        var acc1 = await (await s.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Acme HQ", null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<AccountDto>();
        var acc2 = await (await s.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Acme Subsidiary", null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<AccountDto>();

        var master = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Sarah", "Connor", "sarah@resistance.test", "+15551234", "Director", acc1!.Id))).Content.ReadFromJsonAsync<ContactDto>();
        var dup = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Sarah", "Connor", "sarah@resistance.test", "+15555678", "General", acc1.Id))).Content.ReadFromJsonAsync<ContactDto>();

        // Link duplicate to secondary account via multi-account
        var linkRes = await s.Client.PostAsJsonAsync($"/api/v1/contacts/{dup!.Id}/accounts", new AddContactAccountRequest(acc2!.Id, "Advisor"));
        linkRes.EnsureSuccessStatusCode();

        // Create deal associated with duplicate contact
        var dealRes = await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest(
            "Defense Deal",
            null,
            null,
            50000m,
            null,
            50,
            DateTime.UtcNow.AddMonths(1),
            acc1.Id,
            [new DealContactInput(dup.Id, "Primary Signer", true)],
            null,
            null,
            null
        ));
        dealRes.EnsureSuccessStatusCode();
        var deal = await dealRes.Content.ReadFromJsonAsync<DealDto>();

        // Perform side-by-side merge
        var mergeRes = await s.Client.PostAsJsonAsync($"/api/v1/contacts/{master!.Id}/merge", new MergeContactsRequest(
            [dup.Id],
            new Dictionary<string, string?>
            {
                ["job_title"] = "Commander",
                ["phone"] = "+15551234"
            }
        ));
        mergeRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var merged = await mergeRes.Content.ReadFromJsonAsync<ContactDto>();
        merged!.JobTitle.ShouldBe("Commander");

        // Duplicate contact soft deleted
        (await s.Client.GetAsync($"/api/v1/contacts/{dup.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Deal's contact link re-parented to master contact
        var refreshedDeal = await s.Client.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal!.Id}");
        refreshedDeal!.Contacts.ShouldContain(c => c.ContactId == master.Id);
        refreshedDeal.Contacts.ShouldNotContain(c => c.ContactId == dup.Id);

        // Multi-account link re-parented to master contact
        var masterAccs = await s.Client.GetFromJsonAsync<List<ContactAccountDto>>($"/api/v1/contacts/{master.Id}/accounts");
        masterAccs.ShouldNotBeNull();
        masterAccs.ShouldContain(a => a.AccountId == acc2.Id);
    }

    [Fact]
    public async Task Cross_tenant_merge_or_duplicate_lookup_is_completely_isolated()
    {
        var tenantA = await Workspaces.SignupAsync(factory, "Tenant A");
        var tenantB = await Workspaces.SignupAsync(factory, "Tenant B");
        await EnableFeatureAsync(tenantA, "duplicate-merge");
        await EnableFeatureAsync(tenantB, "duplicate-merge");

        var leadA = await (await tenantA.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Shared", "Person", "shared@domain.test"))).Content.ReadFromJsonAsync<LeadDto>();
        var leadB = await (await tenantB.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Shared", "Person", "shared@domain.test"))).Content.ReadFromJsonAsync<LeadDto>();

        // Tenant A searching duplicates cannot see Tenant B's lead
        var dupsA = await tenantA.Client.GetFromJsonAsync<List<DuplicateCandidateDto>>($"/api/v1/leads/{leadA!.Id}/duplicates");
        dupsA.ShouldNotBeNull();
        dupsA.ShouldNotContain(d => d.Id == leadB!.Id);

        // Tenant A trying to merge Tenant B's lead is rejected with 404
        var crossMerge = await tenantA.Client.PostAsJsonAsync($"/api/v1/leads/{leadA.Id}/merge", new MergeLeadsRequest([leadB.Id]));
        crossMerge.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
