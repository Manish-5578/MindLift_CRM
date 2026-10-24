using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Tickets;

/// <summary>The help desk: opening, assigning, answering and closing tickets, answer times, who sees what.</summary>
public sealed class TicketTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static Task<HttpResponseMessage> Open(Session s, string subject, string? priority = null, Guid? contactId = null, Guid? assignee = null)
        => s.Client.PostAsJsonAsync("/api/v1/tickets", new SaveTicketRequest(subject, "Details", priority, null, contactId, null, assignee));

    private static async Task<TicketDto> Ticket(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<TicketDto>())!;

    [Fact]
    public async Task A_ticket_gets_a_number_a_due_time_from_its_priority_and_can_be_listed_and_found()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var urgent = await Ticket(await Open(admin, "Printer is on fire", "urgent"));
        var normal = await Ticket(await Open(admin, "Question about invoice"));

        urgent.Number.ShouldBe("T-10001");
        normal.Number.ShouldBe("T-10002");
        (urgent.DueAt!.Value - urgent.CreatedAt).TotalHours.ShouldBe(4, 0.1);
        (normal.DueAt!.Value - normal.CreatedAt).TotalHours.ShouldBe(24, 0.1);
        normal.Status.ShouldBe("open");

        var all = (await (await admin.Client.PostAsJsonAsync("/api/v1/tickets/query", new TicketQuery())).Content.ReadFromJsonAsync<PagedResult<TicketListItemDto>>())!;
        all.Total.ShouldBe(2);
        var found = (await (await admin.Client.PostAsJsonAsync("/api/v1/tickets/query", new TicketQuery(Search: "invoice"))).Content.ReadFromJsonAsync<PagedResult<TicketListItemDto>>())!;
        found.Items.Single().Number.ShouldBe("T-10002");
        (await (await admin.Client.PostAsJsonAsync("/api/v1/tickets/query", new TicketQuery(Priority: "urgent"))).Content.ReadFromJsonAsync<PagedResult<TicketListItemDto>>())!.Total.ShouldBe(1);
    }

    [Fact]
    public async Task Bad_tickets_are_refused()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await Open(admin, "")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Open(admin, "Fine", "extreme")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Open(admin, "Fine", contactId: Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Open(admin, "Fine", assignee: Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_first_public_answer_stops_the_clock_a_note_does_not_and_the_ticket_can_be_closed()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var ticket = await Ticket(await Open(admin, "Cannot log in"));
        ticket.FirstResponseAt.ShouldBeNull();

        var afterNote = await Ticket(await admin.Client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new AddTicketCommentRequest("Looks like a password issue", IsInternal: true)));
        afterNote.FirstResponseAt.ShouldBeNull();
        afterNote.Comments.Single().IsInternal.ShouldBeTrue();

        var afterReply = await Ticket(await admin.Client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new AddTicketCommentRequest("We have reset it for you.")));
        afterReply.FirstResponseAt.ShouldNotBeNull();
        afterReply.AssigneeId.ShouldNotBeNull();   // answering a ticket nobody had taken makes it yours
        afterReply.Comments.Count.ShouldBe(2);

        var pending = await Ticket(await admin.Client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest("pending")));
        pending.Status.ShouldBe("pending");
        var resolved = await Ticket(await admin.Client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest("resolved")));
        resolved.ResolvedAt.ShouldNotBeNull();
        (await Ticket(await admin.Client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest("open")))).ResolvedAt.ShouldBeNull();
        (await admin.Client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest("done"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new AddTicketCommentRequest("  "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_reply_can_be_emailed_to_the_customer_who_has_not_unsubscribed()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var address = $"customer-{Guid.NewGuid():N}@example.test";
        var contact = await (await admin.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Cara", "Customer", address, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<ContactDto>();
        var ticket = await Ticket(await Open(admin, "Where is my order?", contactId: contact!.Id));

        (await admin.Client.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new AddTicketCommentRequest("It ships today.", EmailCustomer: true))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var sent = factory.Services.GetService(typeof(RecordingEmailSender)) as RecordingEmailSender;
        sent!.Sent.ShouldContain(m => m.To == address && m.Subject.Contains(ticket.Number) && m.Body.Contains("It ships today."));

        // A ticket with no contact has nobody to email.
        var lonely = await Ticket(await Open(admin, "No contact"));
        (await admin.Client.PostAsJsonAsync($"/api/v1/tickets/{lonely.Id}/comments", new AddTicketCommentRequest("Hello", EmailCustomer: true))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Staff_see_only_their_own_tickets_while_an_admin_sees_all_and_assigning_tells_the_person()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff");
        var staffId = (await staff.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.UserId;

        var mine = await Ticket(await Open(admin, "Assigned to staff", assignee: staffId));
        await Open(admin, "Not for staff");

        var staffView = (await (await staff.Client.PostAsJsonAsync("/api/v1/tickets/query", new TicketQuery())).Content.ReadFromJsonAsync<PagedResult<TicketListItemDto>>())!;
        staffView.Items.Select(t => t.Number).ShouldBe([mine.Number]);
        (await staff.Client.GetAsync($"/api/v1/tickets/{mine.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var adminView = (await (await admin.Client.PostAsJsonAsync("/api/v1/tickets/query", new TicketQuery())).Content.ReadFromJsonAsync<PagedResult<TicketListItemDto>>())!;
        adminView.Total.ShouldBe(2);

        var notes = (await staff.Client.GetFromJsonAsync<NotificationPageDto>("/api/v1/notifications?pageSize=10"))!;
        notes.Items.ShouldContain(n => n.Type == NotificationTypes.TicketAssigned);

        // The other ticket is invisible to staff, not just hidden from the list.
        var other = adminView.Items.Single(t => t.Number != mine.Number);
        (await staff.Client.GetAsync($"/api/v1/tickets/{other.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await staff.Client.PostAsJsonAsync($"/api/v1/tickets/{other.Id}/status", new ChangeTicketStatusRequest("closed"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_summary_counts_open_late_and_unassigned_work_and_another_company_sees_none_of_it()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var other = await Workspaces.SignupAsync(factory);
        await Open(admin, "One");
        var two = await Ticket(await Open(admin, "Two", "high"));
        await admin.Client.PostAsJsonAsync($"/api/v1/tickets/{two.Id}/status", new ChangeTicketStatusRequest("resolved"));

        var summary = (await admin.Client.GetFromJsonAsync<TicketSummaryDto>("/api/v1/tickets/summary"))!;
        summary.Open.ShouldBe(1);
        summary.Unassigned.ShouldBe(1);
        summary.ResolvedLast7Days.ShouldBe(1);
        summary.Late.ShouldBe(0);
        (await other.Client.GetFromJsonAsync<TicketSummaryDto>("/api/v1/tickets/summary"))!.Open.ShouldBe(0);
        (await other.Client.GetAsync($"/api/v1/tickets/{two.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
