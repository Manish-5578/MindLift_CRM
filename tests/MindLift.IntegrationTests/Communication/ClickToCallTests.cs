using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Communication;

public sealed class ClickToCallTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(string first, string last, string email, string phone)
        => new(first, last, email, phone, "Acme Corp", "new", "website", "Warm", null, null, null, null);

    private static SaveContactRequest NewContact(string first, string last, string email, string phone)
        => new(first, last, email, phone, "VP Sales", null, null, null, null, null);

    [Fact]
    public async Task Feature_flag_gates_click_to_call_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Feature is OFF by default
        var respOff = await admin.Client.GetAsync("/api/v1/telephony/settings");
        respOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Turn feature ON
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // Feature endpoint now succeeds
        var respOn = await admin.Client.GetAsync("/api/v1/telephony/settings");
        respOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_and_read_telephony_settings()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Read default settings
        var settingsResp = await admin.Client.GetAsync("/api/v1/telephony/settings");
        settingsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = await settingsResp.Content.ReadFromJsonAsync<TelephonySettingsDto>();

        settings.ShouldNotBeNull();
        settings.Provider.ShouldBe("MockTelephony");
        settings.CallerId.ShouldBe("+18005550199");
        settings.WebRtcEnabled.ShouldBeTrue();

        // 2. Update settings
        var updateResp = await admin.Client.PutAsJsonAsync("/api/v1/telephony/settings", new UpdateTelephonySettingsRequest(
            Provider: "Twilio",
            AccountSid: "AC1234567890abcdef",
            AuthToken: "secret_token_123",
            CallerId: "+15559998888",
            IsActive: true,
            AutoLogCalls: true,
            RecordCallsDefault: true,
            WebRtcEnabled: true
        ));
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<TelephonySettingsDto>();

        updated.ShouldNotBeNull();
        updated.Provider.ShouldBe("Twilio");
        updated.AccountSid.ShouldBe("AC1234567890abcdef");
        updated.CallerId.ShouldBe("+15559998888");
        updated.RecordCallsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Initiate_call_to_lead_and_receive_status_callback()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create Lead
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Jane", "Doe", "jane.doe@example.com", "+15554443322"));
        leadResp.EnsureSuccessStatusCode();
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // 2. Click to call lead
        var callResp = await admin.Client.PostAsJsonAsync("/api/v1/telephony/calls", new InitiateCallRequest(
            ToNumber: "+15554443322",
            LeadId: lead.Id,
            Notes: "Initial outreach call"
        ));
        callResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var callResult = await callResp.Content.ReadFromJsonAsync<InitiateCallResult>();

        callResult.ShouldNotBeNull();
        callResult.Success.ShouldBeTrue();
        callResult.CallSid.ShouldStartWith("CA");
        callResult.Status.ShouldBe("Initiated");
        callResult.WebRtcToken.ShouldNotBeNullOrWhiteSpace();

        // 3. Provider sends status callback (InProgress)
        var anonClient = factory.CreateClient();
        var inProgResp = await anonClient.PostAsJsonAsync("/api/v1/telephony/webhook/status", new UpdateCallStatusRequest(
            CallSid: callResult.CallSid,
            Status: "InProgress"
        ));
        inProgResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 4. Provider sends status callback (Completed)
        var compResp = await anonClient.PostAsJsonAsync("/api/v1/telephony/webhook/status", new UpdateCallStatusRequest(
            CallSid: callResult.CallSid,
            Status: "Completed",
            DurationSeconds: 154
        ));
        compResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var callSession = await compResp.Content.ReadFromJsonAsync<CallSessionDto>();

        callSession.ShouldNotBeNull();
        callSession.Status.ShouldBe("Completed");
        callSession.DurationSeconds.ShouldBe(154);
        callSession.LeadId.ShouldBe(lead.Id);
    }

    [Fact]
    public async Task Initiate_call_to_contact_and_end_call_manually()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create Contact
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Michael", "Scott", "michael@dundermifflin.com", "+15557778899"));
        contactResp.EnsureSuccessStatusCode();
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();

        // 2. Click to call contact
        var callResp = await admin.Client.PostAsJsonAsync("/api/v1/telephony/calls", new InitiateCallRequest(
            ToNumber: "+15557778899",
            ContactId: contact.Id
        ));
        callResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var callResult = await callResp.Content.ReadFromJsonAsync<InitiateCallResult>();
        callResult.ShouldNotBeNull();

        // 3. Rep manually ends call in CRM widget
        var endResp = await admin.Client.PostAsync($"/api/v1/telephony/calls/{callResult.CallId}/end?durationSeconds=88", null);
        endResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var endedCall = await endResp.Content.ReadFromJsonAsync<CallSessionDto>();

        endedCall.ShouldNotBeNull();
        endedCall.Status.ShouldBe("Completed");
        endedCall.DurationSeconds.ShouldBe(88);
        endedCall.ContactId.ShouldBe(contact.Id);

        // 4. Rep updates disposition and notes
        var dispResp = await admin.Client.PostAsJsonAsync($"/api/v1/telephony/calls/{callResult.CallId}/disposition", new UpdateCallDispositionRequest(
            Disposition: "Interested",
            Notes: "Client requested follow-up proposal by Friday."
        ));
        dispResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dispCall = await dispResp.Content.ReadFromJsonAsync<CallSessionDto>();

        dispCall.ShouldNotBeNull();
        dispCall.Disposition.ShouldBe("Interested");
        dispCall.Notes.ShouldContain("follow-up proposal");
    }

    [Fact]
    public async Task Call_history_filtering_by_entity()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var lead1 = await (await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("User", "One", "user1@demo.com", "+15551111111"))).Content.ReadFromJsonAsync<LeadDto>();
        var lead2 = await (await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("User", "Two", "user2@demo.com", "+15552222222"))).Content.ReadFromJsonAsync<LeadDto>();
        lead1.ShouldNotBeNull();
        lead2.ShouldNotBeNull();

        // Make call to lead 1
        await admin.Client.PostAsJsonAsync("/api/v1/telephony/calls", new InitiateCallRequest("+15551111111", LeadId: lead1.Id));
        // Make call to lead 2
        await admin.Client.PostAsJsonAsync("/api/v1/telephony/calls", new InitiateCallRequest("+15552222222", LeadId: lead2.Id));

        // Filter by lead 1
        var historyResp = await admin.Client.GetAsync($"/api/v1/telephony/calls?leadId={lead1.Id}");
        historyResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var calls = await historyResp.Content.ReadFromJsonAsync<List<CallSessionDto>>();

        calls.ShouldNotBeNull();
        calls.Count.ShouldBe(1);
        calls[0].LeadId.ShouldBe(lead1.Id);
    }

    [Fact]
    public async Task Cross_tenant_isolation_is_enforced()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A initiates call
        var callA = await (await tenantA.Client.PostAsJsonAsync("/api/v1/telephony/calls", new InitiateCallRequest("+15553334444"))).Content.ReadFromJsonAsync<InitiateCallResult>();
        callA.ShouldNotBeNull();

        // Tenant A sees the call
        var getCallA = await tenantA.Client.GetAsync($"/api/v1/telephony/calls/{callA.CallId}");
        getCallA.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Tenant B cannot see Tenant A's call
        var getCallB = await tenantB.Client.GetAsync($"/api/v1/telephony/calls/{callA.CallId}");
        getCallB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
