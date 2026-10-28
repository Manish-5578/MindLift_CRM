using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Automation;

public sealed class GdprConsentTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(string first, string last, string email)
        => new(first, last, email, "+1-555-0188", "Titan Enterprises", "new", "website", null, null, null, null, null);

    private static SaveContactRequest NewContact(string first, string last, string email)
        => new(first, last, email, "+1-555-0188", "Privacy Officer", "Customer Contact", null, null, null, null);

    [Fact]
    public async Task Returns_404_when_unsubscribe_center_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var checkResp = await admin.Client.GetAsync("/api/v1/consent/check?email=test@example.com");
        checkResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var tokenResp = await admin.Client.PostAsJsonAsync("/api/v1/consent/tokens/generate", new GeneratePreferenceTokenRequest
        {
            Email = "test@example.com"
        });
        tokenResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var exportResp = await admin.Client.PostAsJsonAsync("/api/v1/consent/export", new GeneratePreferenceTokenRequest
        {
            Email = "test@example.com"
        });
        exportResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var eraseResp = await admin.Client.PostAsJsonAsync("/api/v1/consent/erasure", new GdprErasureRequest
        {
            Email = "test@example.com"
        });
        eraseResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Token_generation_and_public_self_service_preference_center()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/unsubscribe-center", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Generate preference center token
        var genReq = new GeneratePreferenceTokenRequest
        {
            Email = "subscriber@example.com"
        };
        var genResp = await admin.Client.PostAsJsonAsync("/api/v1/consent/tokens/generate", genReq);
        genResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokenDto = await genResp.Content.ReadFromJsonAsync<PreferenceTokenDto>();

        tokenDto.ShouldNotBeNull();
        tokenDto.Token.ShouldNotBeNullOrWhiteSpace();
        tokenDto.Email.ShouldBe("subscriber@example.com");
        tokenDto.PreferenceCenterUrl.ShouldContain(tokenDto.Token);
        tokenDto.OneClickUnsubscribeUrl.ShouldContain(tokenDto.Token);

        // 2. Public client visits preference center
        var publicClient = factory.CreateClient();
        var prefResp = await publicClient.GetAsync($"/api/v1/consent/preferences?token={tokenDto.Token}");
        prefResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pref = await prefResp.Content.ReadFromJsonAsync<PreferenceCenterDetailsDto>();

        pref.ShouldNotBeNull();
        pref.Email.ShouldBe("subscriber@example.com");
        pref.GlobalUnsubscribe.ShouldBeFalse();
        pref.Topics.Count.ShouldBe(4);
        pref.Topics.ShouldContain(t => t.Topic == "Marketing" && t.IsSubscribed);
        pref.Topics.ShouldContain(t => t.Topic == "Sales" && t.IsSubscribed);
        pref.Topics.ShouldContain(t => t.Topic == "ProductUpdates" && t.IsSubscribed);
        pref.Topics.ShouldContain(t => t.Topic == "Surveys" && t.IsSubscribed);
    }

    [Fact]
    public async Task Public_granular_topic_preference_update_and_consent_check()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/unsubscribe-center", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Generate token
        var genResp = await admin.Client.PostAsJsonAsync("/api/v1/consent/tokens/generate", new GeneratePreferenceTokenRequest
        {
            Email = "granular@example.com"
        });
        var tokenDto = await genResp.Content.ReadFromJsonAsync<PreferenceTokenDto>();
        tokenDto.ShouldNotBeNull();

        // 2. Public client updates preferences: Unsubscribe from Marketing & Surveys, keep ProductUpdates
        var publicClient = factory.CreateClient();
        var updateReq = new UpdatePreferencesRequest
        {
            GlobalUnsubscribe = false,
            TopicPreferences = new Dictionary<string, bool>
            {
                ["Marketing"] = false,
                ["ProductUpdates"] = true,
                ["Surveys"] = false
            }
        };

        var putResp = await publicClient.PostAsJsonAsync($"/api/v1/consent/preferences?token={tokenDto.Token}", updateReq);
        putResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. Verify consent check
        var checkMarketing = await admin.Client.GetAsync("/api/v1/consent/check?email=granular@example.com&topic=Marketing");
        checkMarketing.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resMarketing = await checkMarketing.Content.ReadFromJsonAsync<ConsentCheckResult>();
        resMarketing.ShouldNotBeNull();
        resMarketing.IsAllowed.ShouldBeFalse();

        var checkUpdates = await admin.Client.GetAsync("/api/v1/consent/check?email=granular@example.com&topic=ProductUpdates");
        checkUpdates.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resUpdates = await checkUpdates.Content.ReadFromJsonAsync<ConsentCheckResult>();
        resUpdates.ShouldNotBeNull();
        resUpdates.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Rfc_8058_one_click_unsubscribe_header_execution()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/unsubscribe-center", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Generate token
        var genResp = await admin.Client.PostAsJsonAsync("/api/v1/consent/tokens/generate", new GeneratePreferenceTokenRequest
        {
            Email = "oneclick@example.com"
        });
        var tokenDto = await genResp.Content.ReadFromJsonAsync<PreferenceTokenDto>();
        tokenDto.ShouldNotBeNull();

        // 2. Email client triggers RFC 8058 one-click unsubscribe
        var publicClient = factory.CreateClient();
        var oneClickResp = await publicClient.PostAsync($"/api/v1/consent/one-click-unsubscribe?token={tokenDto.Token}", null);
        oneClickResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. Verify consent is now suppressed
        var checkResp = await admin.Client.GetAsync("/api/v1/consent/check?email=oneclick@example.com&topic=Marketing");
        var check = await checkResp.Content.ReadFromJsonAsync<ConsentCheckResult>();
        check.ShouldNotBeNull();
        check.IsAllowed.ShouldBeFalse();

        // 4. Verify audit trail records RFC 8058 source
        var auditResp = await admin.Client.GetAsync("/api/v1/consent/records?email=oneclick@example.com");
        var records = await auditResp.Content.ReadFromJsonAsync<List<GdprConsentRecordDto>>();
        records.ShouldNotBeNull();
        records.ShouldContain(r => r.Source == "OneClickRfc8058" && r.Status == ConsentStatus.OptedOut);
    }

    [Fact]
    public async Task Gdpr_subject_access_request_data_export_article_15_20()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/unsubscribe-center", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var email = "sar.subject@acmewidgets.com";

        // 1. Create a lead and a contact with this email
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Eleanor", "Vance", email));
        leadResp.EnsureSuccessStatusCode();

        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Eleanor", "Vance", email));
        contactResp.EnsureSuccessStatusCode();

        // 2. Record consent history
        await admin.Client.PostAsJsonAsync("/api/v1/consent/records", new SaveConsentRecordRequest
        {
            Email = email,
            Topic = "Marketing",
            Status = ConsentStatus.OptedIn,
            LawfulBasis = GdprLawfulBasis.Consent,
            Source = "WebForm"
        });

        // 3. Export data subject records
        var exportResp = await admin.Client.PostAsJsonAsync("/api/v1/consent/export", new GeneratePreferenceTokenRequest
        {
            Email = email
        });
        exportResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var export = await exportResp.Content.ReadFromJsonAsync<GdprSubjectDataExport>();

        export.ShouldNotBeNull();
        export.SubjectEmail.ShouldBe(email);
        export.Records.Count.ShouldBeGreaterThanOrEqualTo(2);
        export.Records.ShouldContain(r => r.RecordType == "Lead" && r.Email == email);
        export.Records.ShouldContain(r => r.RecordType == "Contact" && r.Email == email);
        export.ConsentHistory.ShouldContain(c => c.Email == email && c.Status == ConsentStatus.OptedIn);
    }

    [Fact]
    public async Task Gdpr_right_to_erasure_right_to_be_forgotten_article_17()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/unsubscribe-center", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var email = "erase.me.now@personaldata.eu";

        // 1. Create Lead and Contact
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Thomas", "Anderson", email));
        leadResp.EnsureSuccessStatusCode();
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Thomas", "Anderson", email));
        contactResp.EnsureSuccessStatusCode();
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();

        // 2. Execute Article 17 Erasure
        var erasureReq = new GdprErasureRequest
        {
            Email = email,
            RequestedBy = "Thomas Anderson",
            Reason = "GDPR Article 17 Erasure Notice"
        };
        var eraseResp = await admin.Client.PostAsJsonAsync("/api/v1/consent/erasure", erasureReq);
        eraseResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await eraseResp.Content.ReadFromJsonAsync<GdprErasureResult>();

        result.ShouldNotBeNull();
        result.Email.ShouldBe(email);
        result.RecordsErased.ShouldBeGreaterThanOrEqualTo(2);

        // 3. Verify lead was anonymized in CRM
        var getLead = await admin.Client.GetAsync($"/api/v1/leads/{lead.Id}");
        getLead.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sanitizedLead = await getLead.Content.ReadFromJsonAsync<LeadDto>();
        sanitizedLead.ShouldNotBeNull();
        sanitizedLead.FirstName.ShouldBe("[Erased]");
        sanitizedLead.LastName.ShouldBe("[GDPR Erased]");
        sanitizedLead.Email.ShouldNotBe(email);
        sanitizedLead.Email.ShouldContain("anonymized.crm");

        // 4. Verify contact was anonymized
        var getContact = await admin.Client.GetAsync($"/api/v1/contacts/{contact.Id}");
        getContact.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sanitizedContact = await getContact.Content.ReadFromJsonAsync<ContactDto>();
        sanitizedContact.ShouldNotBeNull();
        sanitizedContact.FirstName.ShouldBe("[Erased]");
        sanitizedContact.LastName.ShouldBe("[GDPR Erased]");
        sanitizedContact.Email.ShouldNotBe(email);

        // 5. Verify email is now permanently suppressed
        var checkResp = await admin.Client.GetAsync($"/api/v1/consent/check?email={email}");
        var check = await checkResp.Content.ReadFromJsonAsync<ConsentCheckResult>();
        check.ShouldNotBeNull();
        check.IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Multi_tenant_isolation_fails_closed()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/unsubscribe-center", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/unsubscribe-center", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A logs a confidential consent decision
        await tenantA.Client.PostAsJsonAsync("/api/v1/consent/records", new SaveConsentRecordRequest
        {
            Email = "confidential@tenanta.com",
            Topic = "Marketing",
            Status = ConsentStatus.OptedIn,
            Notes = "Secret customer of Tenant A"
        });

        // Tenant B queries records for that email -> should be empty
        var queryB = await tenantB.Client.GetAsync("/api/v1/consent/records?email=confidential@tenanta.com");
        queryB.StatusCode.ShouldBe(HttpStatusCode.OK);
        var recordsB = await queryB.Content.ReadFromJsonAsync<List<GdprConsentRecordDto>>();
        recordsB.ShouldNotBeNull();
        recordsB.ShouldBeEmpty();

        // Tenant B exports data for that email -> records list should be empty
        var exportB = await tenantB.Client.PostAsJsonAsync("/api/v1/consent/export", new GeneratePreferenceTokenRequest
        {
            Email = "confidential@tenanta.com"
        });
        exportB.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dataB = await exportB.Content.ReadFromJsonAsync<GdprSubjectDataExport>();
        dataB.ShouldNotBeNull();
        dataB.Records.ShouldBeEmpty();
        dataB.ConsentHistory.ShouldBeEmpty();
    }
}
