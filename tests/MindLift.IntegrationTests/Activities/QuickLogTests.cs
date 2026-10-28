using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Activities;

/// <summary>
/// Phase 1 / #7: 1-Click Quick-Log Dock (quick-log-dock).
/// Tests fast logging of calls, notes, tasks, and meetings to active records (Lead, Contact, Account, Deal)
/// with automated linking, follow-up scheduling, timeline event publishing, and tenant isolation.
/// </summary>
public sealed class QuickLogTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s, string feature)
        => (await s.Client.PutAsJsonAsync($"/api/v1/features/{feature}", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    private static SaveLeadRequest NewLead(string first, string last, string email)
        => new(first, last, email, null, "Test Co", "new", "website", null, null, null, null, null);

    private static SaveAccountRequest NewAccount(string name)
        => new(name, null, null, null, null, null, null, null);

    private static SaveContactRequest NewContact(string first, string last, Guid? accountId = null)
        => new(first, last, null, null, null, null, accountId, null, null, null);

    [Fact]
    public async Task Feature_flag_off_blocks_quick_log_endpoints()
    {
        var s = await Workspaces.SignupAsync(factory);

        var lead = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("John", "Doe", "john@example.test"))).Content.ReadFromJsonAsync<LeadDto>();

        // Unified endpoint blocked (404)
        var res1 = await s.Client.PostAsJsonAsync("/api/v1/quick-log", new QuickLogRequest(
            TargetType: "lead",
            TargetId: lead!.Id,
            ActivityType: "Call",
            Subject: "Quick intro call"
        ));
        res1.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Convenience endpoint blocked (404)
        var res2 = await s.Client.PostAsJsonAsync("/api/v1/quick-log/call", new QuickLogCallRequest(
            TargetType: "lead",
            TargetId: lead.Id,
            Subject: "Follow-up call"
        ));
        res2.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Feature_flag_on_allows_quick_logging_call_with_followup_and_timeline()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "quick-log-dock");

        var lead = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Sarah", "Connor", "sarah@cyberdyne.test"))).Content.ReadFromJsonAsync<LeadDto>();

        var res = await s.Client.PostAsJsonAsync("/api/v1/quick-log/call", new QuickLogCallRequest(
            TargetType: "lead",
            TargetId: lead!.Id,
            Subject: "Discovery Call",
            Notes: "Discussed cloud migration timeline",
            Direction: "Outbound",
            CallOutcome: "connected",
            DurationMinutes: 20,
            FollowUpSubject: "Send proposal draft",
            FollowUpDueAt: DateTime.UtcNow.AddDays(2)
        ));
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

        var result = await res.Content.ReadFromJsonAsync<QuickLogResult>();
        result.ShouldNotBeNull();

        // Primary call activity verified
        result.Activity.Type.ShouldBe("Call");
        result.Activity.Subject.ShouldBe("Discovery Call");
        result.Activity.Description.ShouldBe("Discussed cloud migration timeline");
        result.Activity.Status.ShouldBe("Completed");
        result.Activity.CallOutcome.ShouldBe("connected");
        result.Activity.DurationMinutes.ShouldBe(20);
        result.Activity.LeadId.ShouldBe(lead.Id);

        // Follow-up task verified
        result.FollowUp.ShouldNotBeNull();
        result.FollowUp.Type.ShouldBe("Task");
        result.FollowUp.Subject.ShouldBe("Send proposal draft");
        result.FollowUp.Status.ShouldBe("Open");
        result.FollowUp.LeadId.ShouldBe(lead.Id);

        // Timeline event published
        using var scope = factory.Services.CreateScope();
        var me = await s.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        using var use = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(me!.TenantId);
        var timeline = scope.ServiceProvider.GetRequiredService<ITimelineEventBus>();
        var events = await timeline.ListAsync("lead", lead.Id);
        events.ShouldContain(e => e.Kind == "call_logged" && e.Summary.Contains("Discovery Call"));
    }

    [Fact]
    public async Task Quick_logging_note_on_contact_derives_account_and_publishes_timeline()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "quick-log-dock");

        var account = await (await s.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Wayne Enterprises"))).Content.ReadFromJsonAsync<AccountDto>();
        var contact = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Bruce", "Wayne", account!.Id))).Content.ReadFromJsonAsync<ContactDto>();

        var res = await s.Client.PostAsJsonAsync("/api/v1/quick-log/note", new QuickLogNoteRequest(
            TargetType: "contact",
            TargetId: contact!.Id,
            Subject: "Q3 Budget Review",
            Notes: "Reviewed quarterly expansion plan"
        ));
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

        var result = await res.Content.ReadFromJsonAsync<QuickLogResult>();
        result.ShouldNotBeNull();
        result.Activity.Type.ShouldBe("Note");
        result.Activity.Status.ShouldBe("Completed");
        result.Activity.ContactId.ShouldBe(contact.Id);
        result.Activity.AccountId.ShouldBe(account.Id);

        // Timeline event
        using var scope = factory.Services.CreateScope();
        var me = await s.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        using var use = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(me!.TenantId);
        var timeline = scope.ServiceProvider.GetRequiredService<ITimelineEventBus>();
        var events = await timeline.ListAsync("contact", contact.Id);
        events.ShouldContain(e => e.Kind == "note_added" && e.Summary.Contains("Q3 Budget Review"));
    }

    [Fact]
    public async Task Quick_logging_meeting_and_task_on_deal_links_properly()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "quick-log-dock");
        await s.Client.PostAsJsonAsync("/api/v1/settings/pipelines", new SavePipelineRequest("Sales", null, true, true, 1));

        var account = await (await s.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Stark Industries"))).Content.ReadFromJsonAsync<AccountDto>();
        var contact = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Tony", "Stark", account!.Id))).Content.ReadFromJsonAsync<ContactDto>();
        var deal = await (await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Arc Reactor Renewal", null, null, 500000m, null, null, null, account.Id, [new(contact!.Id, null, true)], null, null, null))).Content.ReadFromJsonAsync<DealDto>();

        // Meeting on deal
        var meetRes = await s.Client.PostAsJsonAsync("/api/v1/quick-log/meeting", new QuickLogMeetingRequest(
            TargetType: "deal",
            TargetId: deal!.Id,
            Subject: "Contract Negotiation",
            Notes: "Finalize agreement terms",
            StartAt: DateTime.UtcNow.AddDays(3),
            EndAt: DateTime.UtcNow.AddDays(3).AddHours(1),
            Location: "Boardroom A"
        ));
        meetRes.StatusCode.ShouldBe(HttpStatusCode.OK, await meetRes.Content.ReadAsStringAsync());

        var meetResult = await meetRes.Content.ReadFromJsonAsync<QuickLogResult>();
        meetResult.ShouldNotBeNull();
        meetResult.Activity.Type.ShouldBe("Meeting");
        meetResult.Activity.DealId.ShouldBe(deal.Id);
        meetResult.Activity.AccountId.ShouldBe(account.Id);
        meetResult.Activity.ContactId.ShouldBe(contact.Id);
        meetResult.Activity.Location.ShouldBe("Boardroom A");

        // Task on deal
        var taskRes = await s.Client.PostAsJsonAsync("/api/v1/quick-log/task", new QuickLogTaskRequest(
            TargetType: "deal",
            TargetId: deal.Id,
            Subject: "Send updated redline",
            DueAt: DateTime.UtcNow.AddDays(4),
            Priority: "High"
        ));
        taskRes.StatusCode.ShouldBe(HttpStatusCode.OK, await taskRes.Content.ReadAsStringAsync());

        var taskResult = await taskRes.Content.ReadFromJsonAsync<QuickLogResult>();
        taskResult.ShouldNotBeNull();
        taskResult.Activity.Type.ShouldBe("Task");
        taskResult.Activity.DealId.ShouldBe(deal.Id);
        taskResult.Activity.Priority.ShouldBe("High");
    }

    [Fact]
    public async Task Target_validation_and_cross_tenant_isolation_are_strictly_enforced()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(tenantA, "quick-log-dock");

        var tenantB = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(tenantB, "quick-log-dock");

        var leadA = await (await tenantA.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Clark", "Kent", "clark@dailyplanet.test"))).Content.ReadFromJsonAsync<LeadDto>();

        // Invalid target type returns BadRequest (400)
        var invalidTypeRes = await tenantA.Client.PostAsJsonAsync("/api/v1/quick-log", new QuickLogRequest(
            TargetType: "unsupported_entity",
            TargetId: leadA!.Id,
            ActivityType: "Note",
            Subject: "Some note"
        ));
        invalidTypeRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Non-existent target ID returns NotFound (404)
        var nonExistentRes = await tenantA.Client.PostAsJsonAsync("/api/v1/quick-log", new QuickLogRequest(
            TargetType: "lead",
            TargetId: Guid.NewGuid(),
            ActivityType: "Note",
            Subject: "Some note"
        ));
        nonExistentRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Cross-tenant: Tenant B cannot log against Tenant A's lead (404)
        var crossTenantRes = await tenantB.Client.PostAsJsonAsync("/api/v1/quick-log", new QuickLogRequest(
            TargetType: "lead",
            TargetId: leadA.Id,
            ActivityType: "Call",
            Subject: "Cross-tenant intrusion attempt"
        ));
        crossTenantRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
