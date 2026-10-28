using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Communication;

public sealed class SharedInboxTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveContactRequest NewContact(string first, string last, string email, string phone)
        => new(first, last, email, phone, "Head of Support", null, null, null, null, null);

    [Fact]
    public async Task Feature_flag_gates_shared_inbox_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Feature is OFF by default
        var respOff = await admin.Client.GetAsync("/api/v1/shared-inbox/mailboxes");
        respOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Turn feature ON
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/shared-inbox", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // Feature endpoint now succeeds
        var respOn = await admin.Client.GetAsync("/api/v1/shared-inbox/mailboxes");
        respOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Mailbox_lifecycle_and_member_management()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/shared-inbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create mailbox
        var mailboxEmail = $"support_{Guid.NewGuid():N}@mindlift.org";
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/shared-inbox/mailboxes", new CreateSharedMailboxRequest(
            Name: "Support Desk",
            EmailAddress: mailboxEmail,
            Description: "Customer support shared mailbox",
            AutoAssignmentRule: "Manual"));
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var mailbox = await createResp.Content.ReadFromJsonAsync<SharedMailboxDto>();

        mailbox.ShouldNotBeNull();
        mailbox.Name.ShouldBe("Support Desk");
        mailbox.EmailAddress.ShouldBe(mailboxEmail.ToLowerInvariant());
        mailbox.MemberCount.ShouldBe(0);
        mailbox.OpenThreadCount.ShouldBe(0);

        // 2. Add member
        var memberUserId = Guid.NewGuid();
        var addMemberResp = await admin.Client.PostAsJsonAsync($"/api/v1/shared-inbox/mailboxes/{mailbox.Id}/members", new AddMailboxMemberRequest(
            UserId: memberUserId,
            Role: "Agent"));
        addMemberResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var member = await addMemberResp.Content.ReadFromJsonAsync<SharedMailboxMemberDto>();

        member.ShouldNotBeNull();
        member.UserId.ShouldBe(memberUserId);
        member.Role.ShouldBe("Agent");

        // 3. List members
        var membersResp = await admin.Client.GetAsync($"/api/v1/shared-inbox/mailboxes/{mailbox.Id}/members");
        membersResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var members = await membersResp.Content.ReadFromJsonAsync<List<SharedMailboxMemberDto>>();
        members.ShouldNotBeNull();
        members.ShouldContain(m => m.UserId == memberUserId);

        // 4. Update mailbox
        var updateResp = await admin.Client.PutAsJsonAsync($"/api/v1/shared-inbox/mailboxes/{mailbox.Id}", new UpdateSharedMailboxRequest(
            Name: "Global Support",
            EmailAddress: mailboxEmail,
            Description: "Tier 1 and 2 inquiries",
            IsActive: true,
            AutoAssignmentRule: "RoundRobin"));
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<SharedMailboxDto>();
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("Global Support");
        updated.AutoAssignmentRule.ShouldBe("RoundRobin");
        updated.MemberCount.ShouldBe(1);

        // 5. Remove member
        var deleteMemberResp = await admin.Client.DeleteAsync($"/api/v1/shared-inbox/mailboxes/{mailbox.Id}/members/{memberUserId}");
        deleteMemberResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Thread_creation_and_auto_linking_contact()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/shared-inbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create mailbox
        var mailboxResp = await admin.Client.PostAsJsonAsync("/api/v1/shared-inbox/mailboxes", new CreateSharedMailboxRequest(
            Name: "Sales Team",
            EmailAddress: $"sales_{Guid.NewGuid():N}@mindlift.org"));
        mailboxResp.EnsureSuccessStatusCode();
        var mailbox = await mailboxResp.Content.ReadFromJsonAsync<SharedMailboxDto>();
        mailbox.ShouldNotBeNull();

        // 2. Create contact
        var customerEmail = $"vip.client_{Guid.NewGuid():N}@enterprise.com";
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Ada", "Lovelace", customerEmail, "+15556667788"));
        contactResp.EnsureSuccessStatusCode();
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();

        // 3. Create thread in shared inbox from customer
        var threadResp = await admin.Client.PostAsJsonAsync("/api/v1/shared-inbox/threads", new CreateSharedThreadRequest(
            MailboxId: mailbox.Id,
            Subject: "Enterprise billing question",
            CustomerEmail: customerEmail,
            CustomerName: "Ada Lovelace",
            Body: "Could you send over the updated invoice with VAT?",
            Priority: "High"));
        threadResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var thread = await threadResp.Content.ReadFromJsonAsync<SharedThreadDto>();

        thread.ShouldNotBeNull();
        thread.Subject.ShouldBe("Enterprise billing question");
        thread.CustomerEmail.ShouldBe(customerEmail.ToLowerInvariant());
        thread.Status.ShouldBe("Open");
        thread.Priority.ShouldBe("High");
        thread.ContactId.ShouldBe(contact.Id);
        thread.MessageCount.ShouldBe(1);

        // 4. Retrieve thread messages
        var msgsResp = await admin.Client.GetAsync($"/api/v1/shared-inbox/threads/{thread.Id}/messages");
        msgsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var msgs = await msgsResp.Content.ReadFromJsonAsync<List<SharedThreadMessageDto>>();

        msgs.ShouldNotBeNull();
        msgs.Count.ShouldBe(1);
        msgs[0].SenderType.ShouldBe("Customer");
        msgs[0].Body.ShouldContain("updated invoice with VAT");
        msgs[0].IsInternalNote.ShouldBeFalse();
    }

    [Fact]
    public async Task Thread_assignment_and_status_transitions()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/shared-inbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var mailboxResp = await admin.Client.PostAsJsonAsync("/api/v1/shared-inbox/mailboxes", new CreateSharedMailboxRequest(
            Name: "Billing",
            EmailAddress: $"billing_{Guid.NewGuid():N}@mindlift.org"));
        var mailbox = await mailboxResp.Content.ReadFromJsonAsync<SharedMailboxDto>();
        mailbox.ShouldNotBeNull();

        var threadResp = await admin.Client.PostAsJsonAsync("/api/v1/shared-inbox/threads", new CreateSharedThreadRequest(
            MailboxId: mailbox.Id,
            Subject: "Refund request",
            CustomerEmail: "refunds@client.org",
            Body: "Need refund for duplicate transaction"));
        var thread = await threadResp.Content.ReadFromJsonAsync<SharedThreadDto>();
        thread.ShouldNotBeNull();
        thread.AssignedUserId.ShouldBeNull();

        // 1. Assign thread
        var agentId = Guid.NewGuid();
        var assignResp = await admin.Client.PutAsJsonAsync($"/api/v1/shared-inbox/threads/{thread.Id}/assign", new AssignThreadRequest(agentId));
        assignResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var assigned = await assignResp.Content.ReadFromJsonAsync<SharedThreadDto>();

        assigned.ShouldNotBeNull();
        assigned.AssignedUserId.ShouldBe(agentId);
        assigned.AssignedAtUtc.ShouldNotBeNull();

        // 2. Filter threads by assignee
        var queryResp = await admin.Client.GetAsync($"/api/v1/shared-inbox/threads?assignedUserId={agentId}");
        queryResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var filtered = await queryResp.Content.ReadFromJsonAsync<List<SharedThreadDto>>();
        filtered.ShouldNotBeNull();
        filtered.ShouldContain(t => t.Id == thread.Id);

        // 3. Close thread
        var closeResp = await admin.Client.PutAsJsonAsync($"/api/v1/shared-inbox/threads/{thread.Id}/status", new UpdateThreadStatusRequest("Closed"));
        closeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var closed = await closeResp.Content.ReadFromJsonAsync<SharedThreadDto>();

        closed.ShouldNotBeNull();
        closed.Status.ShouldBe("Closed");
        closed.ClosedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Internal_notes_vs_customer_replies_and_first_response_tracking()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/shared-inbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var mailboxResp = await admin.Client.PostAsJsonAsync("/api/v1/shared-inbox/mailboxes", new CreateSharedMailboxRequest(
            Name: "Customer Success",
            EmailAddress: $"cs_{Guid.NewGuid():N}@mindlift.org"));
        var mailbox = await mailboxResp.Content.ReadFromJsonAsync<SharedMailboxDto>();
        mailbox.ShouldNotBeNull();

        var threadResp = await admin.Client.PostAsJsonAsync("/api/v1/shared-inbox/threads", new CreateSharedThreadRequest(
            MailboxId: mailbox.Id,
            Subject: "Onboarding issue",
            CustomerEmail: "newuser@startup.io",
            Body: "I cannot connect my Google calendar"));
        var thread = await threadResp.Content.ReadFromJsonAsync<SharedThreadDto>();
        thread.ShouldNotBeNull();
        thread.FirstResponseAtUtc.ShouldBeNull();

        // 1. Post internal note (agent discussion)
        var noteResp = await admin.Client.PostAsJsonAsync($"/api/v1/shared-inbox/threads/{thread.Id}/messages", new PostThreadMessageRequest(
            Body: "@sarah - can you check if the Google OAuth token is valid in the admin console?",
            IsInternalNote: true));
        noteResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var note = await noteResp.Content.ReadFromJsonAsync<SharedThreadMessageDto>();
        note.ShouldNotBeNull();
        note.IsInternalNote.ShouldBeTrue();

        // Verify thread FirstResponseAtUtc is still null
        var threadAfterNote = await admin.Client.GetFromJsonAsync<SharedThreadDto>($"/api/v1/shared-inbox/threads/{thread.Id}");
        threadAfterNote.ShouldNotBeNull();
        threadAfterNote.FirstResponseAtUtc.ShouldBeNull();
        threadAfterNote.MessageCount.ShouldBe(2);

        // 2. Post customer reply
        var replyResp = await admin.Client.PostAsJsonAsync($"/api/v1/shared-inbox/threads/{thread.Id}/messages", new PostThreadMessageRequest(
            Body: "Hello! We re-authorized your Google Calendar connection. Please refresh your page.",
            IsInternalNote: false));
        replyResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reply = await replyResp.Content.ReadFromJsonAsync<SharedThreadMessageDto>();
        reply.ShouldNotBeNull();
        reply.IsInternalNote.ShouldBeFalse();

        // Verify FirstResponseAtUtc is now set
        var threadAfterReply = await admin.Client.GetFromJsonAsync<SharedThreadDto>($"/api/v1/shared-inbox/threads/{thread.Id}");
        threadAfterReply.ShouldNotBeNull();
        threadAfterReply.FirstResponseAtUtc.ShouldNotBeNull();
        threadAfterReply.MessageCount.ShouldBe(3);
    }

    [Fact]
    public async Task Collision_detection_lock_acquisition_and_release()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/shared-inbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var mailboxResp = await admin.Client.PostAsJsonAsync("/api/v1/shared-inbox/mailboxes", new CreateSharedMailboxRequest(
            Name: "Operations",
            EmailAddress: $"ops_{Guid.NewGuid():N}@mindlift.org"));
        var mailbox = await mailboxResp.Content.ReadFromJsonAsync<SharedMailboxDto>();
        mailbox.ShouldNotBeNull();

        var threadResp = await admin.Client.PostAsJsonAsync("/api/v1/shared-inbox/threads", new CreateSharedThreadRequest(
            MailboxId: mailbox.Id,
            Subject: "Urgent shipment tracking",
            CustomerEmail: "buyer@amazon.com",
            Body: "Where is order #9912?"));
        var thread = await threadResp.Content.ReadFromJsonAsync<SharedThreadDto>();
        thread.ShouldNotBeNull();

        // 1. Agent 1 acquires lock
        var lockResp1 = await admin.Client.PostAsJsonAsync($"/api/v1/shared-inbox/threads/{thread.Id}/lock", new AcquireLockRequest("Drafting"));
        lockResp1.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lock1 = await lockResp1.Content.ReadFromJsonAsync<CollisionStatusDto>();

        lock1.ShouldNotBeNull();
        lock1.HasCollision.ShouldBeFalse();
        lock1.Action.ShouldBe("Drafting");

        // 2. Add second user to same tenant to simulate agent collision
        var agent2 = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff");
        var lockResp2 = await agent2.Client.PostAsJsonAsync($"/api/v1/shared-inbox/threads/{thread.Id}/lock", new AcquireLockRequest("Drafting"));
        lockResp2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lock2 = await lockResp2.Content.ReadFromJsonAsync<CollisionStatusDto>();

        lock2.ShouldNotBeNull();
        lock2.HasCollision.ShouldBeTrue();
        lock2.Action.ShouldBe("Drafting");

        // 3. Agent 1 releases lock
        var releaseResp = await admin.Client.DeleteAsync($"/api/v1/shared-inbox/threads/{thread.Id}/lock");
        releaseResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // 4. Agent 2 acquires lock now -> succeeds with no collision
        var lockResp3 = await agent2.Client.PostAsJsonAsync($"/api/v1/shared-inbox/threads/{thread.Id}/lock", new AcquireLockRequest("Drafting"));
        lockResp3.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lock3 = await lockResp3.Content.ReadFromJsonAsync<CollisionStatusDto>();

        lock3.ShouldNotBeNull();
        lock3.HasCollision.ShouldBeFalse();
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_mailbox_and_thread_access()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/shared-inbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/shared-inbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var mbA = await tenantA.Client.PostAsJsonAsync("/api/v1/shared-inbox/mailboxes", new CreateSharedMailboxRequest(
            Name: "Confidential A",
            EmailAddress: $"confidential_a_{Guid.NewGuid():N}@mindlift.org"));
        var mailboxA = await mbA.Content.ReadFromJsonAsync<SharedMailboxDto>();
        mailboxA.ShouldNotBeNull();

        var thA = await tenantA.Client.PostAsJsonAsync("/api/v1/shared-inbox/threads", new CreateSharedThreadRequest(
            MailboxId: mailboxA.Id,
            Subject: "Confidential Strategy",
            CustomerEmail: "board@company.com",
            Body: "Secret M&A details"));
        var threadA = await thA.Content.ReadFromJsonAsync<SharedThreadDto>();
        threadA.ShouldNotBeNull();

        // Tenant B attempts to get mailbox A
        var crossMb = await tenantB.Client.GetAsync($"/api/v1/shared-inbox/mailboxes/{mailboxA.Id}");
        crossMb.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to get thread A
        var crossTh = await tenantB.Client.GetAsync($"/api/v1/shared-inbox/threads/{threadA.Id}");
        crossTh.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B lists mailboxes -> should not contain mailbox A
        var mailboxesB = await tenantB.Client.GetFromJsonAsync<List<SharedMailboxDto>>("/api/v1/shared-inbox/mailboxes");
        mailboxesB.ShouldNotBeNull();
        mailboxesB.ShouldNotContain(m => m.Id == mailboxA.Id);

        // Tenant B lists threads -> should not contain thread A
        var threadsB = await tenantB.Client.GetFromJsonAsync<List<SharedThreadDto>>("/api/v1/shared-inbox/threads");
        threadsB.ShouldNotBeNull();
        threadsB.ShouldNotContain(t => t.Id == threadA.Id);
    }
}
