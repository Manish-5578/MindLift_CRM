using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Platform;

/// <summary>
/// Phase 2 / #8: Recycle Bin (recycle-bin).
/// Tests listing, restoring, and purging of soft-deleted records across CRM entities
/// with 60-day retention window, user attribution, and multi-tenant isolation.
/// </summary>
public sealed class RecycleBinTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s, string feature)
        => (await s.Client.PutAsJsonAsync($"/api/v1/features/{feature}", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    private static SaveLeadRequest NewLead(string first, string last, string email)
        => new(first, last, email, null, "Recycle Corp", "new", "website", null, null, null, null, null);

    private static SaveAccountRequest NewAccount(string name)
        => new(name, null, null, null, null, null, null, null);

    private static SaveContactRequest NewContact(string first, string last, string email)
        => new(first, last, email, null, null, null, null, null, null, null);

    [Fact]
    public async Task Feature_flag_off_blocks_recycle_bin_endpoints()
    {
        var s = await Workspaces.SignupAsync(factory);

        // List endpoint blocked (404)
        var listRes = await s.Client.GetAsync("/api/v1/recycle-bin");
        listRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Restore endpoint blocked (404)
        var restoreRes = await s.Client.PostAsJsonAsync("/api/v1/recycle-bin/restore", new RestoreRecordsRequest("leads", [Guid.NewGuid()]));
        restoreRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Purge endpoint blocked (404)
        var purgeRes = await s.Client.PostAsJsonAsync("/api/v1/recycle-bin/purge", new PurgeRecordsRequest("leads", [Guid.NewGuid()]));
        purgeRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recycle_bin_lists_deleted_records_with_days_remaining()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "recycle-bin");

        // Create a lead
        var createRes = await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Bruce", "Wayne", "bruce@waynecorp.test"));
        createRes.EnsureSuccessStatusCode();
        var lead = await createRes.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // Soft-delete the lead
        var delRes = await s.Client.DeleteAsync($"/api/v1/leads/{lead.Id}");
        delRes.EnsureSuccessStatusCode();

        // Confirm lead is hidden from normal active view
        var getActiveRes = await s.Client.GetAsync($"/api/v1/leads/{lead.Id}");
        getActiveRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // View in recycle bin
        var listRes = await s.Client.GetFromJsonAsync<RecycleBinPagedResult>("/api/v1/recycle-bin?entityType=leads");
        listRes.ShouldNotBeNull();
        listRes.TotalCount.ShouldBeGreaterThanOrEqualTo(1);

        var item = listRes.Items.FirstOrDefault(x => x.Id == lead.Id);
        item.ShouldNotBeNull();
        item.EntityType.ShouldBe("leads");
        item.DisplayName.ShouldContain("Bruce Wayne");
        item.DaysRemaining.ShouldBe(60);
        item.DeletedByUserName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Recycle_bin_restore_record_succeeds_and_reappears_in_active_queries()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "recycle-bin");

        // Create a lead
        var createRes = await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Clark", "Kent", "clark@dailyplanet.test"));
        var lead = await createRes.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // Delete the lead
        (await s.Client.DeleteAsync($"/api/v1/leads/{lead.Id}")).EnsureSuccessStatusCode();

        // Verify hidden
        var getHiddenRes = await s.Client.GetAsync($"/api/v1/leads/{lead.Id}");
        getHiddenRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Restore via recycle bin
        var restoreRes = await s.Client.PostAsJsonAsync("/api/v1/recycle-bin/restore", new RestoreRecordsRequest("leads", [lead.Id]));
        restoreRes.EnsureSuccessStatusCode();
        var result = await restoreRes.Content.ReadFromJsonAsync<RecycleBinOperationResult>();
        result.ShouldNotBeNull();
        result.ProcessedCount.ShouldBe(1);
        result.SuccessIds.ShouldContain(lead.Id);
        result.Errors.ShouldBeEmpty();

        // Lead is now accessible in active view
        var getRestoredRes = await s.Client.GetAsync($"/api/v1/leads/{lead.Id}");
        getRestoredRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restored = await getRestoredRes.Content.ReadFromJsonAsync<LeadDto>();
        restored.ShouldNotBeNull();
        restored.FirstName.ShouldBe("Clark");

        // Lead no longer in recycle bin
        var binRes = await s.Client.GetFromJsonAsync<RecycleBinPagedResult>("/api/v1/recycle-bin?entityType=leads");
        binRes.ShouldNotBeNull();
        binRes.Items.Any(x => x.Id == lead.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task Recycle_bin_purge_record_permanently_removes_from_database()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "recycle-bin");

        // Create an account
        var createRes = await s.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("LexCorp Industries"));
        createRes.EnsureSuccessStatusCode();
        var account = await createRes.Content.ReadFromJsonAsync<AccountDto>();
        account.ShouldNotBeNull();

        // Delete account
        (await s.Client.DeleteAsync($"/api/v1/accounts/{account.Id}")).EnsureSuccessStatusCode();

        // Confirm present in recycle bin
        var binRes = await s.Client.GetFromJsonAsync<RecycleBinPagedResult>("/api/v1/recycle-bin?entityType=accounts");
        binRes.ShouldNotBeNull();
        binRes.Items.Any(x => x.Id == account.Id).ShouldBeTrue();

        // Purge record
        var purgeRes = await s.Client.PostAsJsonAsync("/api/v1/recycle-bin/purge", new PurgeRecordsRequest("accounts", [account.Id]));
        purgeRes.EnsureSuccessStatusCode();
        var result = await purgeRes.Content.ReadFromJsonAsync<RecycleBinOperationResult>();
        result.ShouldNotBeNull();
        result.ProcessedCount.ShouldBe(1);
        result.SuccessIds.ShouldContain(account.Id);

        // Confirm absent from recycle bin
        var binAfterPurge = await s.Client.GetFromJsonAsync<RecycleBinPagedResult>("/api/v1/recycle-bin?entityType=accounts");
        binAfterPurge.ShouldNotBeNull();
        binAfterPurge.Items.Any(x => x.Id == account.Id).ShouldBeFalse();

        // Confirm permanently deleted from database even with IgnoreQueryFilters
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existsInDb = await db.Accounts.IgnoreQueryFilters().AnyAsync(a => a.Id == account.Id);
        existsInDb.ShouldBeFalse();
    }

    [Fact]
    public async Task Recycle_bin_multi_tenant_isolation_strictly_enforced()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(tenantA, "recycle-bin");

        var tenantB = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(tenantB, "recycle-bin");

        // Tenant A creates and soft-deletes a lead
        var leadA = await (await tenantA.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Diana", "Prince", "diana@themyscira.test"))).Content.ReadFromJsonAsync<LeadDto>();
        leadA.ShouldNotBeNull();
        (await tenantA.Client.DeleteAsync($"/api/v1/leads/{leadA.Id}")).EnsureSuccessStatusCode();

        // Tenant B lists recycle bin -> Tenant A's lead must NOT appear
        var binB = await tenantB.Client.GetFromJsonAsync<RecycleBinPagedResult>("/api/v1/recycle-bin?entityType=leads");
        binB.ShouldNotBeNull();
        binB.Items.Any(x => x.Id == leadA.Id).ShouldBeFalse();

        // Tenant B attempts to restore Tenant A's lead -> 0 restored, fails-closed
        var restoreRes = await tenantB.Client.PostAsJsonAsync("/api/v1/recycle-bin/restore", new RestoreRecordsRequest("leads", [leadA.Id]));
        restoreRes.EnsureSuccessStatusCode();
        var restoreResult = await restoreRes.Content.ReadFromJsonAsync<RecycleBinOperationResult>();
        restoreResult.ShouldNotBeNull();
        restoreResult.ProcessedCount.ShouldBe(0);
        restoreResult.SuccessIds.ShouldNotContain(leadA.Id);

        // Tenant A's lead remains soft-deleted
        var leadCheckA = await tenantA.Client.GetAsync($"/api/v1/leads/{leadA.Id}");
        leadCheckA.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to purge Tenant A's lead -> 0 purged, fails-closed
        var purgeRes = await tenantB.Client.PostAsJsonAsync("/api/v1/recycle-bin/purge", new PurgeRecordsRequest("leads", [leadA.Id]));
        purgeRes.EnsureSuccessStatusCode();
        var purgeResult = await purgeRes.Content.ReadFromJsonAsync<RecycleBinOperationResult>();
        purgeResult.ShouldNotBeNull();
        purgeResult.ProcessedCount.ShouldBe(0);
        purgeResult.SuccessIds.ShouldNotContain(leadA.Id);

        // Tenant A's lead is still in DB (not hard-deleted)
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stillExists = await db.Leads.IgnoreQueryFilters().AnyAsync(l => l.Id == leadA.Id && l.IsDeleted);
        stillExists.ShouldBeTrue();
    }

    [Fact]
    public async Task Recycle_bin_search_filter_returns_matching_records()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "recycle-bin");

        // Create two contacts
        var c1 = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Barry", "Allen", "barry@flash.test"))).Content.ReadFromJsonAsync<ContactDto>();
        var c2 = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Arthur", "Curry", "arthur@atlantis.test"))).Content.ReadFromJsonAsync<ContactDto>();
        c1.ShouldNotBeNull();
        c2.ShouldNotBeNull();

        // Soft-delete both
        (await s.Client.DeleteAsync($"/api/v1/contacts/{c1.Id}")).EnsureSuccessStatusCode();
        (await s.Client.DeleteAsync($"/api/v1/contacts/{c2.Id}")).EnsureSuccessStatusCode();

        // Search for "Barry"
        var searchBarry = await s.Client.GetFromJsonAsync<RecycleBinPagedResult>("/api/v1/recycle-bin?entityType=contacts&search=Barry");
        searchBarry.ShouldNotBeNull();
        searchBarry.Items.Count.ShouldBe(1);
        searchBarry.Items[0].Id.ShouldBe(c1.Id);
        searchBarry.Items[0].DisplayName.ShouldContain("Barry Allen");

        // Search for "Curry"
        var searchCurry = await s.Client.GetFromJsonAsync<RecycleBinPagedResult>("/api/v1/recycle-bin?entityType=contacts&search=Curry");
        searchCurry.ShouldNotBeNull();
        searchCurry.Items.Count.ShouldBe(1);
        searchCurry.Items[0].Id.ShouldBe(c2.Id);
        searchCurry.Items[0].DisplayName.ShouldContain("Arthur Curry");
    }
}
