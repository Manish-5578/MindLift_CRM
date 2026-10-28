using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Communication;

public sealed class EmailSyncTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(string first, string last, string email)
        => new(first, last, email, null, null, "new", "website", null, null, null, null, null);

    private static SaveContactRequest NewContact(string first, string last, string email)
        => new(first, last, email, null, null, null, null, null, null, null);

    [Fact]
    public async Task Returns_404_when_email_sync_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var listResp = await admin.Client.GetAsync("/api/v1/email-sync/accounts");
        listResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var statsResp = await admin.Client.GetAsync("/api/v1/email-sync/stats");
        statsResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var testResp = await admin.Client.PostAsJsonAsync("/api/v1/email-sync/test-connection", new TestMailboxConnectionRequest
        {
            Host = "imap.example.com",
            Username = "user@example.com"
        });
        testResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Mailbox_account_management_and_connection_test()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-sync", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Test mailbox connectivity
        var testReq = new TestMailboxConnectionRequest
        {
            Provider = "IMAP",
            Host = "imap.secureserver.net",
            Port = 993,
            UseSsl = true,
            Username = "rep@acme.com",
            Password = "SecurePassword123!"
        };
        var testResp = await admin.Client.PostAsJsonAsync("/api/v1/email-sync/test-connection", testReq);
        testResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var testResult = await testResp.Content.ReadFromJsonAsync<TestMailboxConnectionResult>();
        testResult.ShouldNotBeNull();
        testResult.Success.ShouldBeTrue();

        // 2. Connect mailbox account
        var createReq = new CreateEmailSyncAccountRequest
        {
            EmailAddress = "rep@acme.com",
            DisplayName = "Alex Rep",
            Provider = "IMAP",
            ImapHost = "imap.secureserver.net",
            ImapPort = 993,
            ImapUseSsl = true,
            ImapUsername = "rep@acme.com",
            Password = "SecurePassword123!",
            SmtpHost = "smtp.secureserver.net",
            SmtpPort = 587,
            SmtpUseSsl = true,
            SyncDirection = "TwoWay",
            EmailVisibility = "Public",
            AutoCreateLeads = false,
            ExcludedDomains = ["acme.com"]
        };
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/email-sync/accounts", createReq);
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var account = await createResp.Content.ReadFromJsonAsync<EmailSyncAccountDto>();

        account.ShouldNotBeNull();
        account.EmailAddress.ShouldBe("rep@acme.com");
        account.DisplayName.ShouldBe("Alex Rep");
        account.SyncDirection.ShouldBe("TwoWay");
        account.EmailVisibility.ShouldBe("Public");

        // 3. Get account by ID
        var getResp = await admin.Client.GetAsync($"/api/v1/email-sync/accounts/{account.Id}");
        getResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<EmailSyncAccountDto>();
        fetched.ShouldNotBeNull();
        fetched.Id.ShouldBe(account.Id);

        // 4. Update account settings
        var updateReq = new UpdateEmailSyncAccountRequest
        {
            EmailVisibility = "Private",
            AutoCreateLeads = true,
            ExcludedDomains = ["acme.com", "internal-mail.com"]
        };
        var updateResp = await admin.Client.PutAsJsonAsync($"/api/v1/email-sync/accounts/{account.Id}", updateReq);
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<EmailSyncAccountDto>();
        updated.ShouldNotBeNull();
        updated.EmailVisibility.ShouldBe("Private");
        updated.AutoCreateLeads.ShouldBeTrue();
        updated.ExcludedDomains.ShouldContain("internal-mail.com");

        // 5. Trigger sync
        var syncResp = await admin.Client.PostAsync($"/api/v1/email-sync/accounts/{account.Id}/sync", null);
        syncResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var syncResult = await syncResp.Content.ReadFromJsonAsync<EmailSyncResultDto>();
        syncResult.ShouldNotBeNull();
        syncResult.Status.ShouldBe("Success");
    }

    [Fact]
    public async Task Inbound_sync_auto_associates_with_existing_lead_and_publishes_timeline()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-sync", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create a lead
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Sarah", "Connor", "sarah.connor@cyberdyne.com"));
        leadResp.EnsureSuccessStatusCode();
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // 2. Connect mailbox
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/email-sync/accounts", new CreateEmailSyncAccountRequest
        {
            EmailAddress = "sales@acme.corp",
            DisplayName = "Acme Sales",
            Provider = "Google"
        });
        createResp.EnsureSuccessStatusCode();
        var account = await createResp.Content.ReadFromJsonAsync<EmailSyncAccountDto>();
        account.ShouldNotBeNull();

        // 3. Inbound email from Sarah Connor
        var inboundReq = new SimulateInboundEmailRequest
        {
            From = "sarah.connor@cyberdyne.com",
            FromName = "Sarah Connor",
            To = "sales@acme.corp",
            Subject = "Inquiry regarding Enterprise License",
            Body = "Hello, we are interested in upgrading our team to MindLift Enterprise. Can you share pricing?",
            MessageId = "<sarah-001@cyberdyne.com>"
        };
        var ingestResp = await admin.Client.PostAsJsonAsync($"/api/v1/email-sync/accounts/{account.Id}/simulate-inbound", inboundReq);
        ingestResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var msg = await ingestResp.Content.ReadFromJsonAsync<EmailSyncMessageDto>();

        msg.ShouldNotBeNull();
        msg.Direction.ShouldBe("Inbound");
        msg.LeadId.ShouldBe(lead.Id);
        msg.ContactId.ShouldBeNull();
        msg.Subject.ShouldBe("Inquiry regarding Enterprise License");

        // 4. Query messages linked to lead
        var listResp = await admin.Client.GetAsync($"/api/v1/email-sync/messages?entityType=lead&entityId={lead.Id}");
        listResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var messages = await listResp.Content.ReadFromJsonAsync<List<EmailSyncMessageDto>>();

        messages.ShouldNotBeNull();
        messages.Count.ShouldBeGreaterThanOrEqualTo(1);
        messages.ShouldContain(m => m.ExternalMessageId == "<sarah-001@cyberdyne.com>" && m.LeadId == lead.Id);

        // 5. Verify stats
        var statsResp = await admin.Client.GetAsync("/api/v1/email-sync/stats");
        statsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stats = await statsResp.Content.ReadFromJsonAsync<EmailSyncStatsDto>();
        stats.ShouldNotBeNull();
        stats.InboundCount.ShouldBeGreaterThanOrEqualTo(1);
        stats.LinkedLeadsCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Inbound_sync_auto_creates_lead_when_configured()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-sync", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Connect mailbox with AutoCreateLeads = true
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/email-sync/accounts", new CreateEmailSyncAccountRequest
        {
            EmailAddress = "inbox@acme.corp",
            DisplayName = "General Inbox",
            Provider = "Microsoft365",
            AutoCreateLeads = true
        });
        createResp.EnsureSuccessStatusCode();
        var account = await createResp.Content.ReadFromJsonAsync<EmailSyncAccountDto>();
        account.ShouldNotBeNull();

        // 2. Inbound email from completely unknown sender
        var inboundReq = new SimulateInboundEmailRequest
        {
            From = "marcus.wright@resistance.org",
            FromName = "Marcus Wright",
            To = "inbox@acme.corp",
            Subject = "Partnership discussion",
            Body = "We would love to explore integrating our system with yours.",
            MessageId = "<marcus-msg-101@resistance.org>"
        };
        var ingestResp = await admin.Client.PostAsJsonAsync($"/api/v1/email-sync/accounts/{account.Id}/simulate-inbound", inboundReq);
        ingestResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var msg = await ingestResp.Content.ReadFromJsonAsync<EmailSyncMessageDto>();

        msg.ShouldNotBeNull();
        msg.LeadId.ShouldNotBeNull();

        // 3. Verify lead was created in CRM Leads database
        var getLeadResp = await admin.Client.GetAsync($"/api/v1/leads/{msg.LeadId.Value}");
        getLeadResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var createdLead = await getLeadResp.Content.ReadFromJsonAsync<LeadDto>();

        createdLead.ShouldNotBeNull();
        createdLead.Email.ShouldBe("marcus.wright@resistance.org");
        createdLead.FirstName.ShouldBe("Marcus");
        createdLead.LastName.ShouldBe("Wright");
        createdLead.Source.ShouldBe("Email Sync");
    }

    [Fact]
    public async Task Excluded_domains_skips_internal_or_competitor_emails()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-sync", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Connect mailbox with internal domain exclusion
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/email-sync/accounts", new CreateEmailSyncAccountRequest
        {
            EmailAddress = "support@acme.corp",
            DisplayName = "Support Mailbox",
            AutoCreateLeads = true,
            ExcludedDomains = ["acme-internal.com", "spamdomain.net"]
        });
        createResp.EnsureSuccessStatusCode();
        var account = await createResp.Content.ReadFromJsonAsync<EmailSyncAccountDto>();
        account.ShouldNotBeNull();

        // Inbound email from excluded domain
        var inboundReq = new SimulateInboundEmailRequest
        {
            From = "hr@acme-internal.com",
            FromName = "Internal HR",
            To = "support@acme.corp",
            Subject = "Company policy update",
            Body = "Please read the updated employee handbook.",
            MessageId = "<internal-hr-99@acme-internal.com>"
        };
        var ingestResp = await admin.Client.PostAsJsonAsync($"/api/v1/email-sync/accounts/{account.Id}/simulate-inbound", inboundReq);
        ingestResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var msg = await ingestResp.Content.ReadFromJsonAsync<EmailSyncMessageDto>();

        msg.ShouldNotBeNull();
        msg.Direction.ShouldBe("Skipped");
        msg.LeadId.ShouldBeNull();
    }

    [Fact]
    public async Task Outbound_email_sending_and_conversation_threading()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/email-sync", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create a contact
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("John", "Connor", "john.connor@resistance.org"));
        contactResp.EnsureSuccessStatusCode();
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();

        // 2. Connect mailbox
        var acctResp = await admin.Client.PostAsJsonAsync("/api/v1/email-sync/accounts", new CreateEmailSyncAccountRequest
        {
            EmailAddress = "account.exec@acme.corp",
            DisplayName = "Acme Exec"
        });
        acctResp.EnsureSuccessStatusCode();
        var account = await acctResp.Content.ReadFromJsonAsync<EmailSyncAccountDto>();
        account.ShouldNotBeNull();

        // 3. Send outbound email from CRM
        var sendReq = new SendSyncEmailRequest
        {
            To = "john.connor@resistance.org",
            Subject = "Proposal for MindLift CRM Implementation",
            Body = "Hi John,\n\nPlease find our detailed proposal attached.\n\nBest,\nAcme",
            ContactId = contact.Id
        };
        var sendResp = await admin.Client.PostAsJsonAsync($"/api/v1/email-sync/accounts/{account.Id}/send", sendReq);
        sendResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sentMsg = await sendResp.Content.ReadFromJsonAsync<EmailSyncMessageDto>();

        sentMsg.ShouldNotBeNull();
        sentMsg.Direction.ShouldBe("Outbound");
        sentMsg.ContactId.ShouldBe(contact.Id);
        sentMsg.ThreadId.ShouldNotBeNullOrWhiteSpace();

        // 4. Inbound reply referencing the outbound email
        var replyReq = new SimulateInboundEmailRequest
        {
            From = "john.connor@resistance.org",
            FromName = "John Connor",
            To = "account.exec@acme.corp",
            Subject = "Re: Proposal for MindLift CRM Implementation",
            Body = "Thanks for the proposal! We will review it this week.",
            InReplyTo = sentMsg.ExternalMessageId
        };
        var replyResp = await admin.Client.PostAsJsonAsync($"/api/v1/email-sync/accounts/{account.Id}/simulate-inbound", replyReq);
        replyResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replyMsg = await replyResp.Content.ReadFromJsonAsync<EmailSyncMessageDto>();

        replyMsg.ShouldNotBeNull();
        replyMsg.Direction.ShouldBe("Inbound");
        replyMsg.ContactId.ShouldBe(contact.Id);
        replyMsg.ThreadId.ShouldBe(sentMsg.ThreadId); // Shared conversation thread!
    }

    [Fact]
    public async Task Strict_multi_tenant_isolation_and_privacy_fails_closed()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/email-sync", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/email-sync", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A connects account and sends email
        var createResp = await tenantA.Client.PostAsJsonAsync("/api/v1/email-sync/accounts", new CreateEmailSyncAccountRequest
        {
            EmailAddress = "confidential@company-a.com",
            DisplayName = "Company A Executive"
        });
        createResp.EnsureSuccessStatusCode();
        var accountA = await createResp.Content.ReadFromJsonAsync<EmailSyncAccountDto>();
        accountA.ShouldNotBeNull();

        await tenantA.Client.PostAsJsonAsync($"/api/v1/email-sync/accounts/{accountA.Id}/send", new SendSyncEmailRequest
        {
            To = "client@domain.com",
            Subject = "Confidential Contract Terms",
            Body = "Strictly confidential."
        });

        // Tenant B attempts to read Tenant A's account -> 404
        var crossGetAcct = await tenantB.Client.GetAsync($"/api/v1/email-sync/accounts/{accountA.Id}");
        crossGetAcct.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to trigger sync on Tenant A's account -> 404
        var crossSync = await tenantB.Client.PostAsync($"/api/v1/email-sync/accounts/{accountA.Id}/sync", null);
        crossSync.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B lists messages -> completely empty
        var msgsResp = await tenantB.Client.GetAsync("/api/v1/email-sync/messages");
        msgsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var msgsB = await msgsResp.Content.ReadFromJsonAsync<List<EmailSyncMessageDto>>();
        msgsB.ShouldNotBeNull();
        msgsB.ShouldBeEmpty();

        // Tenant B stats -> 0 accounts, 0 messages
        var statsResp = await tenantB.Client.GetAsync("/api/v1/email-sync/stats");
        statsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var statsB = await statsResp.Content.ReadFromJsonAsync<EmailSyncStatsDto>();
        statsB.ShouldNotBeNull();
        statsB.TotalAccounts.ShouldBe(0);
        statsB.TotalMessagesSynced.ShouldBe(0);
    }
}
