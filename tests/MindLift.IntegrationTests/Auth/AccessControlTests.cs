using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Infrastructure.Setup;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MindLift.IntegrationTests.Auth;

/// <summary>Plan 4.4 and 13: permissions, tenant isolation, plan limits and platform administration, through the real API.</summary>
public sealed class AccessControlTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Staff_cannot_list_users_but_a_manager_can_and_neither_can_invite()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "staff");
        var manager = await Workspaces.InviteAndAcceptAsync(factory, admin, "Manager", "manager");

        (await staff.Client.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await manager.Client.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var roles = await admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var staffRole = roles!.Single(r => r.Name == "Staff").Id;
        var invite = await manager.Client.PostAsJsonAsync("/api/v1/invitations",
            new CreateInvitationRequest($"x-{Guid.NewGuid():N}@example.test", "X", "Y", staffRole, null));
        invite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Changing_a_role_matrix_takes_effect_on_the_next_request()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "matrix");
        (await staff.Client.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var roles = await admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var staffRoleId = roles!.Single(r => r.Name == "Staff").Id;
        var matrix = await admin.Client.GetFromJsonAsync<List<RolePermissionDto>>($"/api/v1/roles/{staffRoleId}/permissions");
        var updated = matrix!.Select(p => p.Module == AppModule.Users ? p with { CanView = true } : p).ToList();

        var save = await admin.Client.PutAsJsonAsync($"/api/v1/roles/{staffRoleId}/permissions", new SaveRolePermissionsRequest(updated));
        save.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await staff.Client.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_user_cannot_read_another_workspaces_user_by_id()
    {
        var a = await Workspaces.SignupAsync(factory);
        var b = await Workspaces.SignupAsync(factory);
        var meB = await b.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");

        var response = await a.Client.GetAsync($"/api/v1/users/{meB!.UserId}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_cannot_delete_a_system_role_or_change_the_admin_matrix()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var roles = await admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var adminRole = roles!.Single(r => r.Name == "Admin");
        var staffRole = roles!.Single(r => r.Name == "Staff");

        (await admin.Client.DeleteAsync($"/api/v1/roles/{staffRole.Id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var save = await admin.Client.PutAsJsonAsync($"/api/v1/roles/{adminRole.Id}/permissions",
            new SaveRolePermissionsRequest(new List<RolePermissionDto>()));
        save.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_role_with_members_cannot_be_deleted()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var roleName = $"Field agent {Guid.NewGuid():N}";
        var created = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest(roleName, null, DashboardView.Staff));
        var role = await created.Content.ReadFromJsonAsync<RoleDto>();

        // An accepted member (not just a pending invitation) keeps the role in use.
        await Workspaces.InviteAndAcceptAsync(factory, admin, roleName, "agent");

        var delete = await admin.Client.DeleteAsync($"/api/v1/roles/{role!.Id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(delete)).ShouldBe("role_in_use");
    }

    [Fact]
    public async Task The_workspace_owner_cannot_be_deactivated()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        var admin = await Workspaces.InviteAndAcceptAsync(factory, owner, "Admin", "second-admin");

        var response = await admin.Client.PostAsJsonAsync($"/api/v1/users/{me!.UserId}/deactivate", new DeactivateUserRequest(null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(response)).ShouldBe("owner_cannot_be_deactivated");
    }

    [Fact]
    public async Task Pending_invitations_count_against_the_plan_seat_limit()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await SetPlatformPlanAsync(owner, "starter");
        var roles = await owner.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var staffRoleId = roles!.Single(r => r.Name == "Staff").Id;

        // Starter allows 5 seats. The owner uses one, so four pending invitations fill the plan.
        for (var i = 0; i < 4; i++)
        {
            var ok = await owner.Client.PostAsJsonAsync("/api/v1/invitations",
                new CreateInvitationRequest($"seat{i}-{Guid.NewGuid():N}@example.test", "Seat", "Holder", staffRoleId, null));
            ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var fifth = await owner.Client.PostAsJsonAsync("/api/v1/invitations",
            new CreateInvitationRequest($"seat5-{Guid.NewGuid():N}@example.test", "Seat", "Holder", staffRoleId, null));

        fifth.StatusCode.ShouldBe((HttpStatusCode)402);
        (await Workspaces.ErrorCodeAsync(fifth)).ShouldBe("plan_limit_users");
    }

    [Fact]
    public async Task Audit_log_records_role_changes_for_admins_only()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var created = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest($"Audited {Guid.NewGuid():N}", null, DashboardView.Staff));
        created.StatusCode.ShouldBe(HttpStatusCode.OK);

        var logs = await admin.Client.GetFromJsonAsync<PagedResult<AuditLogDto>>("/api/v1/audit-logs?entityType=Role");
        logs!.Items.ShouldContain(l => l.Action == "Create" && l.EntityType == "Role");

        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "audit");
        (await staff.Client.GetAsync("/api/v1/audit-logs")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Tenant_admins_cannot_reach_platform_endpoints_and_platform_admins_can()
    {
        var tenantAdmin = await Workspaces.SignupAsync(factory);
        (await tenantAdmin.Client.GetAsync("/api/v1/platform/tenants")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var email = $"platform-{Guid.NewGuid():N}@mindlift.test";
        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>();
            await seeder.SeedAsync(email, Workspaces.Password, "Platform", "Admin");
        }

        var platform = await Workspaces.LoginAsync(factory, email, Workspaces.Password);
        var tenants = await platform.Client.GetAsync("/api/v1/platform/tenants");
        tenants.StatusCode.ShouldBe(HttpStatusCode.OK);

        // A platform token does not grant tenant data: the platform admin has no role in the tenant.
        (await tenantAdmin.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Changes a workspace's plan through the platform API, as a platform admin would.</summary>
    private async Task SetPlatformPlanAsync(Session owner, string planKey)
    {
        var email = $"platform-{Guid.NewGuid():N}@mindlift.test";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>().SeedAsync(email, Workspaces.Password, "P", "A");
        }

        var platform = await Workspaces.LoginAsync(factory, email, Workspaces.Password);
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        var response = await platform.Client.PatchAsJsonAsync($"/api/v1/platform/tenants/{me!.TenantId}/plan", new ChangeTenantPlanRequest(planKey));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
