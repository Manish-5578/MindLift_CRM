using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Tickets;

/// <summary>Assigning, re-statusing and deleting many tickets at once from the help desk list.</summary>
public sealed class TicketBulkTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task<TicketDto> OpenAsync(Session s, string subject, Guid? assignee = null)
        => (await (await s.Client.PostAsJsonAsync("/api/v1/tickets", new SaveTicketRequest(subject, "Details", null, null, null, null, assignee))).Content.ReadFromJsonAsync<TicketDto>())!;

    private static async Task<TicketDto> GetAsync(Session s, Guid id) => (await s.Client.GetFromJsonAsync<TicketDto>($"/api/v1/tickets/{id}"))!;

    private static async Task<BulkActionResult> BulkAsync(Session s, BulkActionRequest request)
    {
        var response = await s.Client.PostAsJsonAsync("/api/v1/tickets/bulk", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BulkActionResult>())!;
    }

    [Fact]
    public async Task Many_tickets_can_be_assigned_to_one_person_at_once_and_that_person_is_told()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff");
        var staffId = (await staff.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.UserId;
        var a = await OpenAsync(admin, "First");
        var b = await OpenAsync(admin, "Second");

        var result = await BulkAsync(admin, new BulkActionRequest(BulkActions.Assign, [a.Id, b.Id], null, OwnerId: staffId));

        (result.Requested, result.Succeeded, result.Failures.Count).ShouldBe((2, 2, 0));
        (await GetAsync(admin, a.Id)).AssigneeId.ShouldBe(staffId);
        (await GetAsync(admin, b.Id)).AssigneeId.ShouldBe(staffId);
        var notes = (await staff.Client.GetFromJsonAsync<NotificationPageDto>("/api/v1/notifications?pageSize=10"))!;
        notes.Items.Count(n => n.Type == NotificationTypes.TicketAssigned).ShouldBe(2);
    }

    [Fact]
    public async Task Many_tickets_can_change_status_at_once_and_finishing_them_stamps_the_resolved_time()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var a = await OpenAsync(admin, "First");
        var b = await OpenAsync(admin, "Second");

        var result = await BulkAsync(admin, new BulkActionRequest(BulkActions.ChangeStatus, [a.Id, b.Id], null, Status: "resolved"));

        result.Succeeded.ShouldBe(2);
        (await GetAsync(admin, a.Id)).Status.ShouldBe("resolved");
        (await GetAsync(admin, b.Id)).ResolvedAt.ShouldNotBeNull();
        (await BulkAsync(admin, new BulkActionRequest(BulkActions.ChangeStatus, [a.Id], null, Status: "open"))).Succeeded.ShouldBe(1);
        (await GetAsync(admin, a.Id)).ResolvedAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_ticket_that_cannot_be_changed_is_reported_and_the_rest_still_go_ahead()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var real = await OpenAsync(admin, "Real one");
        var ghost = Guid.NewGuid();

        var result = await BulkAsync(admin, new BulkActionRequest(BulkActions.ChangeStatus, [real.Id, ghost], null, Status: "closed"));

        (result.Requested, result.Succeeded).ShouldBe((2, 1));
        result.Failures.Single().Id.ShouldBe(ghost);
        (await GetAsync(admin, real.Id)).Status.ShouldBe("closed");
    }

    [Fact]
    public async Task Bulk_requests_with_nothing_chosen_a_wrong_action_or_a_wrong_status_are_refused()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var t = await OpenAsync(admin, "Only");
        (await admin.Client.PostAsJsonAsync("/api/v1/tickets/bulk", new BulkActionRequest(BulkActions.Assign, [], null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync("/api/v1/tickets/bulk", new BulkActionRequest(BulkActions.AddTags, [t.Id], null, Tags: ["x"]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync("/api/v1/tickets/bulk", new BulkActionRequest(BulkActions.ChangeStatus, [t.Id], null, Status: "done"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var stranger = await BulkAsync(admin, new BulkActionRequest(BulkActions.Assign, [t.Id], null, OwnerId: Guid.NewGuid()));
        (stranger.Succeeded, stranger.Failures.Count).ShouldBe((0, 1));
        (await GetAsync(admin, t.Id)).AssigneeId.ShouldBeNull();
    }

    [Fact]
    public async Task Many_tickets_can_be_deleted_at_once()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var a = await OpenAsync(admin, "Gone one");
        var b = await OpenAsync(admin, "Gone two");
        var keep = await OpenAsync(admin, "Keeper");

        var response = await admin.Client.PostAsJsonAsync("/api/v1/tickets/bulk-delete", new BulkDeleteRequest([a.Id, b.Id], null));
        var result = (await response.Content.ReadFromJsonAsync<BulkActionResult>())!;

        (result.Requested, result.Succeeded).ShouldBe((2, 2));
        (await admin.Client.GetAsync($"/api/v1/tickets/{a.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.Client.GetAsync($"/api/v1/tickets/{keep.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.Client.PostAsJsonAsync("/api/v1/tickets/bulk-delete", new BulkDeleteRequest([], null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Staff_can_never_change_tickets_outside_their_scope_and_other_companies_cannot_touch_mine()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff");
        var other = await Workspaces.SignupAsync(factory);
        var hidden = await OpenAsync(admin, "Not for staff");

        var staffTry = await staff.Client.PostAsJsonAsync("/api/v1/tickets/bulk", new BulkActionRequest(BulkActions.ChangeStatus, [hidden.Id], null, Status: "closed"));
        if (staffTry.StatusCode == HttpStatusCode.OK) (await staffTry.Content.ReadFromJsonAsync<BulkActionResult>())!.Succeeded.ShouldBe(0);
        else staffTry.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var otherTry = await BulkAsync(other, new BulkActionRequest(BulkActions.ChangeStatus, [hidden.Id], null, Status: "closed"));
        otherTry.Succeeded.ShouldBe(0);
        (await staff.Client.PostAsJsonAsync("/api/v1/tickets/bulk-delete", new BulkDeleteRequest([hidden.Id], null))).StatusCode.ShouldNotBe(HttpStatusCode.NoContent);
        (await GetAsync(admin, hidden.Id)).Status.ShouldBe("open");
    }

    [Fact]
    public async Task Bulk_ticket_actions_need_a_signed_in_user()
    {
        var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/tickets/bulk", new BulkActionRequest(BulkActions.Assign, [Guid.NewGuid()], null))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/tickets/bulk-delete", new BulkDeleteRequest([Guid.NewGuid()], null))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
