using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Security;

public sealed class AccessControlTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task User_With_Users_Edit_Permission_Cannot_Escalate_User_To_Admin()
    {
        var admin = await Workspaces.SignupAsync(factory, "EscalationTest Corp");

        var roles = await admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var adminRoleId = roles!.Single(r => r.IsAdmin).Id;

        // 1. Admin creates a custom role "HR Coordinator"
        var createRoleRes = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("HR Coordinator", "Can edit users", DashboardView.Staff));
        createRoleRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var hrRole = await createRoleRes.Content.ReadFromJsonAsync<RoleDto>();

        // 2. Admin grants HR Coordinator permission to Edit and Create users, but NOT Admin
        var savePermRes = await admin.Client.PutAsJsonAsync($"/api/v1/roles/{hrRole!.Id}/permissions", new SaveRolePermissionsRequest([
            new RolePermissionDto(AppModule.Users, CanView: true, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.All),
            new RolePermissionDto(AppModule.Leads, CanView: true, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.All)
        ]));
        savePermRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. Invite HR user
        var hrUser = await Workspaces.InviteAndAcceptAsync(factory, admin, "HR Coordinator", "hr-user");

        // 4. Invite regular staff member
        var staffMember = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "staff-target");
        var usersList = await admin.Client.GetFromJsonAsync<PagedResult<UserListItem>>($"/api/v1/users?search={staffMember.Email}");
        var targetUserId = usersList!.Items.Single().Id;

        // 5. HR user tries to escalate staff member (or themselves) to Admin!
        var updatePayload = new UpdateUserRequest("Staff", "Target", null, "Staff", "en", adminRoleId, null, null, null);
        var escalateResponse = await hrUser.Client.PutAsJsonAsync($"/api/v1/users/{targetUserId}", updatePayload);

        // Must be forbidden (CWE-269 Privilege Escalation check)
        escalateResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task User_With_Users_Create_Permission_Cannot_Invite_Admin()
    {
        var admin = await Workspaces.SignupAsync(factory, "InviteEscalation Corp");

        var roles = await admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var adminRoleId = roles!.Single(r => r.IsAdmin).Id;

        // Admin creates a custom recruiter role with Users.Create
        var createRoleRes = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("Recruiter", "Can invite users", DashboardView.Staff));
        createRoleRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var recruiterRole = await createRoleRes.Content.ReadFromJsonAsync<RoleDto>();

        await admin.Client.PutAsJsonAsync($"/api/v1/roles/{recruiterRole!.Id}/permissions", new SaveRolePermissionsRequest([
            new RolePermissionDto(AppModule.Users, CanView: true, CanCreate: true, CanEdit: false, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.All)
        ]));

        var recruiter = await Workspaces.InviteAndAcceptAsync(factory, admin, "Recruiter", "recruiter");

        // Recruiter attempts to invite an Admin
        var rogueEmail = $"rogue-admin-{Guid.NewGuid():N}@example.test";
        var response = await recruiter.Client.PostAsJsonAsync("/api/v1/invitations",
            new CreateInvitationRequest(rogueEmail, "Rogue", "Admin", adminRoleId, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NonAdmin_Cannot_Modify_Role_Permissions()
    {
        var admin = await Workspaces.SignupAsync(factory, "RolePermissions Corp");

        var roles = await admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var staffRoleId = roles!.Single(r => r.Name == "Staff").Id;

        // Create user editor role with Users.Edit
        var createRoleRes = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest("User Editor", "Can edit users", DashboardView.Staff));
        var editorRole = await createRoleRes.Content.ReadFromJsonAsync<RoleDto>();

        await admin.Client.PutAsJsonAsync($"/api/v1/roles/{editorRole!.Id}/permissions", new SaveRolePermissionsRequest([
            new RolePermissionDto(AppModule.Users, CanView: true, CanCreate: true, CanEdit: true, CanDelete: false, CanExport: false, CanImport: false, Scope: DataScope.All)
        ]));

        var editor = await Workspaces.InviteAndAcceptAsync(factory, admin, "User Editor", "editor");

        // User editor attempts to grant full Settings access to Staff role
        var saveReq = new SaveRolePermissionsRequest([
            new RolePermissionDto(AppModule.Settings, true, true, true, true, true, true, DataScope.All)
        ]);

        var response = await editor.Client.PutAsJsonAsync($"/api/v1/roles/{staffRoleId}/permissions", saveReq);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deactivated_User_Token_Immediately_Rejected()
    {
        var admin = await Workspaces.SignupAsync(factory, "Deactivation Corp");
        var member = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "deact-target");

        // Member verifies active access works on /api/v1/auth/me
        var initialCheck = await member.Client.GetAsync("/api/v1/auth/me");
        initialCheck.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Get member's ID and deactivate them
        var usersList = await admin.Client.GetFromJsonAsync<PagedResult<UserListItem>>($"/api/v1/users?search={member.Email}");
        var memberId = usersList!.Items.Single().Id;

        var deactResponse = await admin.Client.PostAsync($"/api/v1/users/{memberId}/deactivate", null);
        deactResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Member's existing JWT token should immediately be rejected
        var subsequentCheck = await member.Client.GetAsync("/api/v1/auth/me");
        subsequentCheck.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
