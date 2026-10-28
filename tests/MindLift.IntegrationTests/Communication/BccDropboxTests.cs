using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Communication;

public sealed class BccDropboxTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(string first, string last, string email, string company = "Acme Inc")
        => new(first, last, email, "+15550199", company, "new", "website", "Warm", null, null, null, null);

    private static SaveAccountRequest NewAccount(string name)
        => new(name, "https://apex.com", "+15551122", "Finance", null, null, null, null);

    private static SaveContactRequest NewContact(string first, string last, string email, Guid? accountId = null)
        => new(first, last, email, "+15553344", "Manager", null, accountId, null, null, null);

    [Fact]
    public async Task Feature_flag_gates_bcc_dropbox_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Feature is OFF by default
        var respOff = await admin.Client.GetAsync("/api/v1/bcc-dropbox/my-address");
        respOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Turn feature ON
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/bcc-dropbox", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // Feature endpoint now succeeds
        var respOn = await admin.Client.GetAsync("/api/v1/bcc-dropbox/my-address");
        respOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_and_regenerate_dropbox_address()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/bcc-dropbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Get address (auto-provisions if not existing)
        var resp = await admin.Client.GetAsync("/api/v1/bcc-dropbox/my-address");
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var addr1 = await resp.Content.ReadFromJsonAsync<BccDropboxAddressDto>();

        addr1.ShouldNotBeNull();
        addr1.DropboxToken.ShouldNotBeNullOrWhiteSpace();
        addr1.DropboxEmail.ShouldContain(addr1.DropboxToken);
        addr1.IsActive.ShouldBeTrue();
        addr1.TotalEmailsReceived.ShouldBe(0);

        // 2. Regenerate address
        var regenResp = await admin.Client.PostAsync("/api/v1/bcc-dropbox/regenerate-address", null);
        regenResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var addr2 = await regenResp.Content.ReadFromJsonAsync<BccDropboxAddressDto>();

        addr2.ShouldNotBeNull();
        addr2.DropboxToken.ShouldNotBe(addr1.DropboxToken);
        addr2.DropboxEmail.ShouldContain(addr2.DropboxToken);
    }

    [Fact]
    public async Task Update_and_read_bcc_settings()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/bcc-dropbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Read default settings
        var settingsResp = await admin.Client.GetAsync("/api/v1/bcc-dropbox/settings");
        settingsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = await settingsResp.Content.ReadFromJsonAsync<BccDropboxSettingsDto>();

        settings.ShouldNotBeNull();
        settings.DropboxDomain.ShouldBe("bcc.mindlift.crm");
        settings.AutoCreateLead.ShouldBeFalse();

        // 2. Update settings
        var updateResp = await admin.Client.PutAsJsonAsync("/api/v1/bcc-dropbox/settings", new UpdateBccDropboxSettingsRequest(
            DropboxDomain: "mail.company.com",
            AutoCreateLead: true,
            AutoCreateContact: false,
            MatchDeals: true,
            DefaultPrivacy: "Private",
            ExcludedDomains: ["internal.corp", "competitor.com"],
            LogNonMatched: true
        ));
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<BccDropboxSettingsDto>();

        updated.ShouldNotBeNull();
        updated.DropboxDomain.ShouldBe("mail.company.com");
        updated.AutoCreateLead.ShouldBeTrue();
        updated.DefaultPrivacy.ShouldBe("Private");
        updated.ExcludedDomains.ShouldContain("internal.corp");
    }

    [Fact]
    public async Task Inbound_email_matches_existing_lead()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/bcc-dropbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Get rep address
        var addrResp = await admin.Client.GetAsync("/api/v1/bcc-dropbox/my-address");
        var addr = await addrResp.Content.ReadFromJsonAsync<BccDropboxAddressDto>();
        addr.ShouldNotBeNull();

        // 2. Create a lead
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Sarah", "Connor", "sarah.connor@cyberdyne.org", "Cyberdyne Systems"));
        leadResp.EnsureSuccessStatusCode();
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // 3. Post inbound BCC email via anonymous webhook
        var anonClient = factory.CreateClient();
        var inboundResp = await anonClient.PostAsJsonAsync("/api/v1/bcc-dropbox/inbound", new InboundBccEmailRequest
        {
            Token = addr.DropboxToken,
            From = "rep@mindlift.com",
            To = ["sarah.connor@cyberdyne.org"],
            Subject = "Demo confirmation for next Tuesday",
            BodyPlain = "Hi Sarah, confirming our demo next Tuesday at 2pm.",
            MessageId = "<msg-101@mail.google.com>",
            DateUtc = DateTime.UtcNow
        });

        inboundResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await inboundResp.Content.ReadFromJsonAsync<InboundBccEmailResult>();

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.ActionTaken.ShouldBe("MatchedExisting");
        result.MatchedLeadId.ShouldBe(lead.Id);
        result.CreatedMessageId.ShouldNotBeNull();

        // 4. Verify address counter incremented
        var updatedAddrResp = await admin.Client.GetAsync("/api/v1/bcc-dropbox/my-address");
        var updatedAddr = await updatedAddrResp.Content.ReadFromJsonAsync<BccDropboxAddressDto>();
        updatedAddr.ShouldNotBeNull();
        updatedAddr.TotalEmailsReceived.ShouldBe(1);

        // 5. Verify log recorded
        var logsResp = await admin.Client.GetAsync("/api/v1/bcc-dropbox/logs");
        logsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var logs = await logsResp.Content.ReadFromJsonAsync<List<BccDropboxLogDto>>();

        logs.ShouldNotBeNull();
        logs.ShouldContain(l => l.MatchedLeadId == lead.Id && l.Subject == "Demo confirmation for next Tuesday");
    }

    [Fact]
    public async Task Inbound_email_matches_contact_and_open_deal()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/bcc-dropbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Get address
        var addr = await (await admin.Client.GetAsync("/api/v1/bcc-dropbox/my-address")).Content.ReadFromJsonAsync<BccDropboxAddressDto>();
        addr.ShouldNotBeNull();

        // 2. Create Account
        var acctResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Apex Holdings"));
        acctResp.EnsureSuccessStatusCode();
        var acct = await acctResp.Content.ReadFromJsonAsync<AccountDto>();
        acct.ShouldNotBeNull();

        // 3. Create Contact
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Alex", "Morgan", "alex.morgan@apex.com", acct.Id));
        contactResp.EnsureSuccessStatusCode();
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();

        // 4. Create Deal with contact
        var dealResp = await admin.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest(
            Name: "Apex Q4 Contract",
            PipelineId: null,
            StageId: null,
            Amount: 75000m,
            Currency: null,
            Probability: 50,
            ExpectedCloseDate: null,
            AccountId: acct.Id,
            Contacts: [new DealContactInput(contact.Id, "DecisionMaker", true)],
            Description: "Deal created for bcc test",
            OwnerId: null,
            CustomFields: null
        ));
        dealResp.EnsureSuccessStatusCode();
        var deal = await dealResp.Content.ReadFromJsonAsync<DealDto>();
        deal.ShouldNotBeNull();

        // 5. Inbound BCC email
        var anonClient = factory.CreateClient();
        var inboundResp = await anonClient.PostAsJsonAsync("/api/v1/bcc-dropbox/inbound", new InboundBccEmailRequest
        {
            Token = addr.DropboxToken,
            From = "rep@mindlift.com",
            To = ["alex.morgan@apex.com"],
            Subject = "Proposal details for Apex Q4",
            BodyPlain = "Hi Alex, here is the proposal we discussed."
        });

        inboundResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await inboundResp.Content.ReadFromJsonAsync<InboundBccEmailResult>();

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.MatchedContactId.ShouldBe(contact.Id);
        result.MatchedAccountId.ShouldBe(acct.Id);
        result.MatchedDealId.ShouldBe(deal.Id);
    }

    [Fact]
    public async Task Auto_creates_lead_when_enabled_and_no_match()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/bcc-dropbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Enable auto-create lead
        await admin.Client.PutAsJsonAsync("/api/v1/bcc-dropbox/settings", new UpdateBccDropboxSettingsRequest(
            DropboxDomain: null,
            AutoCreateLead: true,
            AutoCreateContact: false,
            MatchDeals: false,
            DefaultPrivacy: null,
            ExcludedDomains: null,
            LogNonMatched: true
        ));

        var addr = await (await admin.Client.GetAsync("/api/v1/bcc-dropbox/my-address")).Content.ReadFromJsonAsync<BccDropboxAddressDto>();
        addr.ShouldNotBeNull();

        // Inbound BCC email to an un-tracked recipient
        var anonClient = factory.CreateClient();
        var inboundResp = await anonClient.PostAsJsonAsync("/api/v1/bcc-dropbox/inbound", new InboundBccEmailRequest
        {
            Token = addr.DropboxToken,
            From = "rep@mindlift.com",
            To = ["elena.rostova@novatech.co"],
            Subject = "Introduction to MindLift CRM",
            BodyPlain = "Hi Elena, nice meeting you today."
        });

        inboundResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await inboundResp.Content.ReadFromJsonAsync<InboundBccEmailResult>();

        result.ShouldNotBeNull();
        result.ActionTaken.ShouldBe("CreatedLead");
        result.MatchedLeadId.ShouldNotBeNull();

        // Verify newly created lead
        var getLead = await admin.Client.GetAsync($"/api/v1/leads/{result.MatchedLeadId}");
        getLead.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lead = await getLead.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();
        lead.Email.ShouldBe("elena.rostova@novatech.co");
        lead.CompanyName.ShouldBe("Novatech.co");
    }

    [Fact]
    public async Task Excluded_domains_are_skipped()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/bcc-dropbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Configure excluded domain
        await admin.Client.PutAsJsonAsync("/api/v1/bcc-dropbox/settings", new UpdateBccDropboxSettingsRequest(
            DropboxDomain: null,
            AutoCreateLead: true,
            AutoCreateContact: false,
            MatchDeals: false,
            DefaultPrivacy: null,
            ExcludedDomains: ["internalcorp.com"],
            LogNonMatched: true
        ));

        var addr = await (await admin.Client.GetAsync("/api/v1/bcc-dropbox/my-address")).Content.ReadFromJsonAsync<BccDropboxAddressDto>();
        addr.ShouldNotBeNull();

        // Send to excluded domain
        var anonClient = factory.CreateClient();
        var inboundResp = await anonClient.PostAsJsonAsync("/api/v1/bcc-dropbox/inbound", new InboundBccEmailRequest
        {
            Token = addr.DropboxToken,
            From = "rep@internalcorp.com",
            To = ["vp@internalcorp.com"],
            Subject = "Internal strategy meeting",
            BodyPlain = "Private notes"
        });

        inboundResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await inboundResp.Content.ReadFromJsonAsync<InboundBccEmailResult>();

        result.ShouldNotBeNull();
        result.ActionTaken.ShouldBe("SkippedExcludedDomain");
        result.MatchedLeadId.ShouldBeNull();
    }

    [Fact]
    public async Task Cross_tenant_isolation_is_enforced()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/bcc-dropbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/bcc-dropbox", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var addrA = await (await tenantA.Client.GetAsync("/api/v1/bcc-dropbox/my-address")).Content.ReadFromJsonAsync<BccDropboxAddressDto>();
        addrA.ShouldNotBeNull();

        // Send email to Tenant A
        var anon = factory.CreateClient();
        await anon.PostAsJsonAsync("/api/v1/bcc-dropbox/inbound", new InboundBccEmailRequest
        {
            Token = addrA.DropboxToken,
            From = "repA@mindlift.com",
            To = ["clientA@domain.com"],
            Subject = "Confidential Tenant A message"
        });

        // Tenant A sees 1 log
        var logsA = await (await tenantA.Client.GetAsync("/api/v1/bcc-dropbox/logs")).Content.ReadFromJsonAsync<List<BccDropboxLogDto>>();
        logsA.ShouldNotBeNull();
        logsA.Count.ShouldBe(1);

        // Tenant B sees 0 logs (no leak!)
        var logsB = await (await tenantB.Client.GetAsync("/api/v1/bcc-dropbox/logs")).Content.ReadFromJsonAsync<List<BccDropboxLogDto>>();
        logsB.ShouldNotBeNull();
        logsB.Count.ShouldBe(0);
    }
}
