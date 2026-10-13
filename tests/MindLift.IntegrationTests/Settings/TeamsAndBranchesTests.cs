using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Settings;

/// <summary>Plan 7.1 teams and branches: every write goes through rules so no record can point at something that does not belong to the workspace.</summary>
public sealed class TeamsAndBranchesTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task<BranchDto> BranchAsync(Session admin, string name, string? zone = "Asia/Dubai")
    {
        var response = await admin.Client.PostAsJsonAsync("/api/v1/branches", new SaveBranchRequest(name, "Office 1", zone));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BranchDto>())!;
    }

    private static async Task<TeamDto> TeamAsync(Session admin, string name, Guid? branch = null, Guid? leader = null)
    {
        var response = await admin.Client.PostAsJsonAsync("/api/v1/teams", new SaveTeamRequest(name, branch, leader));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TeamDto>())!;
    }

    private static async Task<Guid> UserIdAsync(Session session) => (await session.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.UserId;

    private static async Task PutInTeamAsync(Session admin, Guid userId, Guid? teamId)
    {
        var user = (await admin.Client.GetFromJsonAsync<UserDetail>($"/api/v1/users/{userId}"))!;
        var response = await admin.Client.PutAsJsonAsync($"/api/v1/users/{userId}", new UpdateUserRequest(
            user.FirstName, user.LastName, user.Phone, user.JobTitle, user.Language, user.RoleId, teamId, user.BranchId, user.ManagerId));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_team_can_be_created_renamed_and_listed_with_its_member_count()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "team-member");
        var team = await TeamAsync(admin, "Sales");
        await PutInTeamAsync(admin, await UserIdAsync(staff), team.Id);

        var renamed = await admin.Client.PutAsJsonAsync($"/api/v1/teams/{team.Id}", new SaveTeamRequest("Sales UAE", null, null));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await admin.Client.GetFromJsonAsync<List<TeamDto>>("/api/v1/teams");
        var found = list!.Single();
        found.Name.ShouldBe("Sales UAE");
        found.MemberCount.ShouldBe(1);
    }

    [Fact]
    public async Task Saving_a_team_returns_its_real_member_count()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "count-member");
        var team = await TeamAsync(admin, "Support");
        await PutInTeamAsync(admin, await UserIdAsync(staff), team.Id);

        var response = await admin.Client.PutAsJsonAsync($"/api/v1/teams/{team.Id}", new SaveTeamRequest("Support", null, null));
        var saved = await response.Content.ReadFromJsonAsync<TeamDto>();

        saved!.MemberCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_team_leader_must_be_a_person_in_this_workspace()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var other = await Workspaces.SignupAsync(factory);
        var strangerId = await UserIdAsync(other);

        var nobody = await admin.Client.PostAsJsonAsync("/api/v1/teams", new SaveTeamRequest("Ghosts", null, Guid.NewGuid()));
        var stranger = await admin.Client.PostAsJsonAsync("/api/v1/teams", new SaveTeamRequest("Spies", null, strangerId));

        ((int)nobody.StatusCode).ShouldBeInRange(400, 499);
        ((int)stranger.StatusCode).ShouldBeInRange(400, 499);
        (await admin.Client.GetFromJsonAsync<List<TeamDto>>("/api/v1/teams"))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_team_leader_can_be_a_member_of_the_workspace()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "leader");
        var staffId = await UserIdAsync(staff);

        var team = await TeamAsync(admin, "Led team", leader: staffId);

        team.LeaderId.ShouldBe(staffId);
    }

    [Fact]
    public async Task A_team_name_is_unique_ignoring_surrounding_spaces()
    {
        var admin = await Workspaces.SignupAsync(factory);
        await TeamAsync(admin, "Sales");

        var again = await admin.Client.PostAsJsonAsync("/api/v1/teams", new SaveTeamRequest("  Sales ", null, null));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(again)).ShouldBe("team_name_taken");
    }

    [Fact]
    public async Task A_team_can_keep_its_own_name_when_saved_again()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var team = await TeamAsync(admin, "Sales");

        var response = await admin.Client.PutAsJsonAsync($"/api/v1/teams/{team.Id}", new SaveTeamRequest("Sales", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_team_with_members_cannot_be_deleted_until_they_move()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "delete-member");
        var team = await TeamAsync(admin, "Temp");
        var staffId = await UserIdAsync(staff);
        await PutInTeamAsync(admin, staffId, team.Id);

        var refused = await admin.Client.DeleteAsync($"/api/v1/teams/{team.Id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("team_in_use");

        await PutInTeamAsync(admin, staffId, null);
        (await admin.Client.DeleteAsync($"/api/v1/teams/{team.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_team_cannot_point_at_a_branch_that_does_not_exist_or_belongs_to_someone_else()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var other = await Workspaces.SignupAsync(factory);
        var foreignBranch = await BranchAsync(other, "Foreign");

        var missing = await admin.Client.PostAsJsonAsync("/api/v1/teams", new SaveTeamRequest("A", Guid.NewGuid(), null));
        var foreign = await admin.Client.PostAsJsonAsync("/api/v1/teams", new SaveTeamRequest("B", foreignBranch.Id, null));

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_branch_with_teams_cannot_be_deleted_and_a_free_one_can()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var used = await BranchAsync(admin, "Dubai");
        var free = await BranchAsync(admin, "Abu Dhabi");
        await TeamAsync(admin, "Dubai sales", used.Id);

        var refused = await admin.Client.DeleteAsync($"/api/v1/branches/{used.Id}");
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("branch_in_use");
        (await admin.Client.DeleteAsync($"/api/v1/branches/{free.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_branch_time_zone_must_be_a_real_one()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var bad = await admin.Client.PostAsJsonAsync("/api/v1/branches", new SaveBranchRequest("Nowhere", null, "Mars/Olympus_Mons"));
        var good = await admin.Client.PostAsJsonAsync("/api/v1/branches", new SaveBranchRequest("Dubai", null, "Asia/Dubai"));
        var none = await admin.Client.PostAsJsonAsync("/api/v1/branches", new SaveBranchRequest("Default zone", null, null));

        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        good.StatusCode.ShouldBe(HttpStatusCode.OK);
        none.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_branch_name_is_unique()
    {
        var admin = await Workspaces.SignupAsync(factory);
        await BranchAsync(admin, "Dubai");

        var again = await admin.Client.PostAsJsonAsync("/api/v1/branches", new SaveBranchRequest(" Dubai ", null, null));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(again)).ShouldBe("branch_name_taken");
    }

    [Fact]
    public async Task Another_workspace_cannot_see_change_or_delete_these_teams_and_branches()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var other = await Workspaces.SignupAsync(factory);
        var team = await TeamAsync(admin, "Private");
        var branch = await BranchAsync(admin, "Private HQ");

        (await other.Client.GetFromJsonAsync<List<TeamDto>>("/api/v1/teams"))!.ShouldBeEmpty();
        (await other.Client.GetFromJsonAsync<List<BranchDto>>("/api/v1/branches"))!.ShouldBeEmpty();
        (await other.Client.PutAsJsonAsync($"/api/v1/teams/{team.Id}", new SaveTeamRequest("Hijacked", null, null))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Client.DeleteAsync($"/api/v1/teams/{team.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Client.PutAsJsonAsync($"/api/v1/branches/{branch.Id}", new SaveBranchRequest("Hijacked", null, null))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.Client.DeleteAsync($"/api/v1/branches/{branch.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_cannot_change_teams_or_branches()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "no-teams");
        var team = await TeamAsync(admin, "Locked");

        (await staff.Client.PostAsJsonAsync("/api/v1/teams", new SaveTeamRequest("Nope", null, null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.DeleteAsync($"/api/v1/teams/{team.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.PostAsJsonAsync("/api/v1/branches", new SaveBranchRequest("Nope", null, null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
