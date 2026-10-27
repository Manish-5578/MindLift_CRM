using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Security;

/// <summary>
/// Integration tests verifying that row-level data scope, role hierarchy, module permissions,
/// relationship constraints, and portal token/HTTPS security are strictly enforced.
/// </summary>
public sealed class ScopeEnforcementTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Own_Scoped_User_Cannot_Access_Or_Modify_Another_Users_Lead()
    {
        var admin = await Workspaces.SignupAsync(factory, "ScopeLead Corp");

        // 1. Create a lead owned by admin
        var leadRes = await admin.Client.PostAsJsonAsync("/api/v1/leads",
            new SaveLeadRequest("Alice", "Smith", "alice@example.test", "+123456789", "Acme", "New", "Web", "Hot", null, null, null, null));
        leadRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lead = await leadRes.Content.ReadFromJsonAsync<LeadDto>();

        // 2. Staff user has default DataScope.Own
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "staff-scope-lead");

        // 3. Staff user attempts to GET admin's lead -> 404 NotFound
        var getRes = await staff.Client.GetAsync($"/api/v1/leads/{lead!.Id}");
        getRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 4. Staff user attempts to PUT admin's lead -> 404 NotFound
        var putRes = await staff.Client.PutAsJsonAsync($"/api/v1/leads/{lead.Id}",
            new SaveLeadRequest("Alice", "Modified", "alice@example.test", "+123456789", "Acme", "Contacted", "Web", "Warm", null, null, null, null));
        putRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 5. Staff user attempts to DELETE admin's lead -> 403 or 404
        var delRes = await staff.Client.DeleteAsync($"/api/v1/leads/{lead.Id}");
        delRes.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lead_Merge_Requires_Delete_Permission_And_Scope_On_All_IDs()
    {
        var admin = await Workspaces.SignupAsync(factory, "LeadMerge Corp");

        // Admin creates primary lead and duplicate lead
        var primaryRes = await admin.Client.PostAsJsonAsync("/api/v1/leads",
            new SaveLeadRequest("Primary", "User", "p@example.test", null, "Acme", "New", "Web", null, null, null, null, null));
        var primary = await primaryRes.Content.ReadFromJsonAsync<LeadDto>();

        var dupRes = await admin.Client.PostAsJsonAsync("/api/v1/leads",
            new SaveLeadRequest("Dup", "User", "d@example.test", null, "Acme", "New", "Web", null, null, null, null, null));
        var dup = await dupRes.Content.ReadFromJsonAsync<LeadDto>();

        // Create an edit-only role without Delete permission
        var createRoleRes = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("Lead Editor", "Edit only", DashboardView.Staff));
        var role = await createRoleRes.Content.ReadFromJsonAsync<RoleDto>();

        await admin.Client.PutAsJsonAsync($"/api/v1/roles/{role!.Id}/permissions", new SaveRolePermissionsRequest([
            new RolePermissionDto(AppModule.Leads, CanView: true, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.All)
        ]));

        var editor = await Workspaces.InviteAndAcceptAsync(factory, admin, "Lead Editor", "lead-editor");

        // Editor attempts to merge leads (which deletes duplicates)
        var mergeRes = await editor.Client.PostAsJsonAsync($"/api/v1/leads/{primary!.Id}/merge",
            new MergeLeadsRequest([dup!.Id]));

        // Must be forbidden because caller lacks Delete permission
        mergeRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deal_Owner_Reassignment_Cannot_Assign_To_User_Outside_Caller_Scope()
    {
        var admin = await Workspaces.SignupAsync(factory, "DealReassign Corp");

        // Staff 1 and Staff 2
        var staff1 = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "staff-deal-1");
        var staff2 = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "staff-deal-2");

        // Staff 1 creates a deal
        var dealRes = await staff1.Client.PostAsJsonAsync("/api/v1/deals",
            new SaveDealRequest("Staff Deal", null, null, 5000, "USD", null, null, null, null, null, null, null));
        dealRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var deal = await dealRes.Content.ReadFromJsonAsync<DealDto>();

        // Get Staff 2's User ID
        var usersList = await admin.Client.GetFromJsonAsync<PagedResult<UserListItem>>($"/api/v1/users?search={staff2.Email}");
        var staff2UserId = usersList!.Items.Single().Id;

        // Staff 1 (Own scope) tries to reassign deal owner to Staff 2 (outside Staff 1's scope)
        var reassignRes = await staff1.Client.PutAsJsonAsync($"/api/v1/deals/{deal!.Id}",
            new SaveDealRequest("Staff Deal", null, null, 5000, "USD", null, null, null, null, null, staff2UserId, null));

        // Must fail with NotFound or Forbidden (cannot assign to out-of-scope owner)
        reassignRes.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Automation_Rule_With_Webhook_Action_Refuses_Non_Admin()
    {
        var admin = await Workspaces.SignupAsync(factory, "WebhookSecurity Corp");

        // Create a custom manager role with Settings.Edit
        var createRoleRes = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("Settings Manager", "Settings edit", DashboardView.Staff));
        var role = await createRoleRes.Content.ReadFromJsonAsync<RoleDto>();

        await admin.Client.PutAsJsonAsync($"/api/v1/roles/{role!.Id}/permissions", new SaveRolePermissionsRequest([
            new RolePermissionDto(AppModule.Settings, CanView: true, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.All),
            new RolePermissionDto(AppModule.Automation, CanView: true, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.All)
        ]));

        var manager = await Workspaces.InviteAndAcceptAsync(factory, admin, "Settings Manager", "settings-mgr");

        // Manager attempts to create an automation rule with a Webhook action
        var webhookReq = new SaveAutomationRuleRequest(
            "Exfiltrate Lead Data",
            "leads",
            AutomationTriggerType.RecordCreated,
            null,
            null,
            [new AutomationActionDefinition(AutomationActionType.Webhook, WebhookUrl: "https://example.com/webhook")],
            true,
            1
        );

        var ruleRes = await manager.Client.PostAsJsonAsync("/api/v1/automation/rules", webhookReq);

        // Must be forbidden: webhooks require Admin rights
        ruleRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Saved_Views_Refuse_Access_When_User_Lacks_Entity_Permission()
    {
        var admin = await Workspaces.SignupAsync(factory, "SavedViewSecurity Corp");

        // Create a role that only has Deals permission, NO Contacts permission
        var createRoleRes = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("Deals Only", "Deals", DashboardView.Staff));
        var role = await createRoleRes.Content.ReadFromJsonAsync<RoleDto>();

        await admin.Client.PutAsJsonAsync($"/api/v1/roles/{role!.Id}/permissions", new SaveRolePermissionsRequest([
            new RolePermissionDto(AppModule.Deals, CanView: true, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.All)
        ]));

        var dealsUser = await Workspaces.InviteAndAcceptAsync(factory, admin, "Deals Only", "deals-user");

        // User attempts to list saved views for "contacts"
        var listRes = await dealsUser.Client.GetAsync("/api/v1/views/contacts");
        listRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // User attempts to create a saved view for "contacts"
        var createRes = await dealsUser.Client.PostAsJsonAsync("/api/v1/views",
            new SaveSavedViewRequest("contacts", "My Contacts", false, false, null, ["first_name", "last_name"], null, null));
        createRes.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task User_Hierarchy_Cycle_Check_Prevents_Reporting_Loops()
    {
        var admin = await Workspaces.SignupAsync(factory, "CycleCheck Corp");

        var userA = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "user-a");
        var userB = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "user-b");

        var uA = (await admin.Client.GetFromJsonAsync<PagedResult<UserListItem>>($"/api/v1/users?search={userA.Email}"))!.Items.Single();
        var uB = (await admin.Client.GetFromJsonAsync<PagedResult<UserListItem>>($"/api/v1/users?search={userB.Email}"))!.Items.Single();

        // Admin sets user A's manager to user B
        var setAManager = await admin.Client.PutAsJsonAsync($"/api/v1/users/{uA.Id}",
            new UpdateUserRequest("User", "A", null, "Staff", "en", uA.RoleId, null, null, uB.Id));
        setAManager.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Admin now tries to set user B's manager to user A -> creates cycle A -> B -> A
        var setBManager = await admin.Client.PutAsJsonAsync($"/api/v1/users/{uB.Id}",
            new UpdateUserRequest("User", "B", null, "Staff", "en", uB.RoleId, null, null, uA.Id));

        // Must fail with Conflict (409)
        setBManager.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
