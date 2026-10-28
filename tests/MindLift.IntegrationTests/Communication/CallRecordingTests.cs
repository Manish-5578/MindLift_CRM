using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Communication;

public sealed class CallRecordingTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(string first, string last, string email, string phone)
        => new(first, last, email, phone, "Acme AI Corp", "new", "website", "Warm", null, null, null, null);

    [Fact]
    public async Task Feature_flag_gates_call_recording_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Feature is OFF by default
        var respOff = await admin.Client.GetAsync("/api/v1/telephony/recording/consent");
        respOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Turn feature ON
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/call-recording", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // Feature endpoint now succeeds
        var respOn = await admin.Client.GetAsync("/api/v1/telephony/recording/consent");
        respOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_and_read_recording_consent_settings()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/call-recording", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Read default consent settings
        var getResp = await admin.Client.GetAsync("/api/v1/telephony/recording/consent");
        getResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var consent = await getResp.Content.ReadFromJsonAsync<RecordingConsentDto>();

        consent.ShouldNotBeNull();
        consent.ConsentMode.ShouldBe("Prompt");
        consent.DualPartyNotification.ShouldBeTrue();
        consent.RetentionDays.ShouldBe(90);

        // 2. Update consent settings
        var putResp = await admin.Client.PutAsJsonAsync("/api/v1/telephony/recording/consent", new UpdateRecordingConsentRequest(
            ConsentMode: "Always",
            DualPartyNotification: false,
            RetentionDays: 180));
        putResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await putResp.Content.ReadFromJsonAsync<RecordingConsentDto>();

        updated.ShouldNotBeNull();
        updated.ConsentMode.ShouldBe("Always");
        updated.DualPartyNotification.ShouldBeFalse();
        updated.RetentionDays.ShouldBe(180);

        // 3. Validation rejection on invalid mode
        var invalidResp = await admin.Client.PutAsJsonAsync("/api/v1/telephony/recording/consent", new UpdateRecordingConsentRequest(
            ConsentMode: "InvalidMode",
            DualPartyNotification: true,
            RetentionDays: 30));
        invalidResp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 4. Validation rejection on invalid retention days
        var invalidDaysResp = await admin.Client.PutAsJsonAsync("/api/v1/telephony/recording/consent", new UpdateRecordingConsentRequest(
            ConsentMode: "Always",
            DualPartyNotification: true,
            RetentionDays: 99999));
        invalidDaysResp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transcribe_call_session_generates_segments_summary_and_sentiment()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/call-recording", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create Lead and initiate call
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Marcus", "Aurelius", "marcus@rome.org", "+15551239876"));
        leadResp.EnsureSuccessStatusCode();
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        var callResp = await admin.Client.PostAsJsonAsync("/api/v1/telephony/calls", new InitiateCallRequest(
            ToNumber: "+15551239876",
            LeadId: lead.Id,
            Record: true));
        callResp.EnsureSuccessStatusCode();
        var call = await callResp.Content.ReadFromJsonAsync<InitiateCallResult>();
        call.ShouldNotBeNull();

        // Complete the call
        var endResp = await admin.Client.HttpPostAsync($"/api/v1/telephony/calls/{call.CallId}/end?durationSeconds=80", null);
        endResp.EnsureSuccessStatusCode();

        // 2. Transcribe call
        var transcribeResp = await admin.Client.PostAsJsonAsync($"/api/v1/telephony/recording/calls/{call.CallId}/transcribe", new TranscribeCallRequest());
        transcribeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var transcript = await transcribeResp.Content.ReadFromJsonAsync<TranscriptionResultDto>();

        transcript.ShouldNotBeNull();
        transcript.CallId.ShouldBe(call.CallId);
        transcript.Status.ShouldBe("Completed");
        transcript.FullText.ShouldNotBeNullOrWhiteSpace();
        transcript.FullText.ShouldContain("MindLift CRM today");
        transcript.Summary.ShouldNotBeNullOrWhiteSpace();
        transcript.Sentiment.ShouldBe("Positive");
        transcript.ActionItems.Count.ShouldBeGreaterThanOrEqualTo(2);
        transcript.Segments.Count.ShouldBeGreaterThanOrEqualTo(5);

        // 3. Read transcription endpoint
        var readResp = await admin.Client.GetAsync($"/api/v1/telephony/recording/calls/{call.CallId}/transcription");
        readResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var readTranscript = await readResp.Content.ReadFromJsonAsync<TranscriptionResultDto>();

        readTranscript.ShouldNotBeNull();
        readTranscript.FullText.ShouldBe(transcript.FullText);
        readTranscript.Summary.ShouldBe(transcript.Summary);
        readTranscript.Sentiment.ShouldBe(transcript.Sentiment);
    }

    [Fact]
    public async Task Process_external_transcription_provider_webhook()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/call-recording", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create Call
        var callResp = await admin.Client.PostAsJsonAsync("/api/v1/telephony/calls", new InitiateCallRequest(
            ToNumber: "+15557776655",
            Record: true));
        callResp.EnsureSuccessStatusCode();
        var call = await callResp.Content.ReadFromJsonAsync<InitiateCallResult>();
        call.ShouldNotBeNull();

        // 2. Anonymous STT provider webhook fires
        var anonymousClient = factory.CreateClient();
        var webhookResp = await anonymousClient.PostAsJsonAsync("/api/v1/telephony/recording/webhook/transcription", new TranscriptionWebhookRequest(
            CallSid: call.CallSid,
            Status: "Completed",
            FullText: "Agent: Welcome to our demo.\nContact: Thanks, looks very intuitive.",
            Summary: "Prospect confirmed system is intuitive.",
            Sentiment: "Neutral",
            ActionItems: ["Send follow-up deck"]));
        webhookResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. Verify tenant sees the updated transcription
        var readResp = await admin.Client.GetAsync($"/api/v1/telephony/recording/calls/{call.CallId}/transcription");
        readResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var transcript = await readResp.Content.ReadFromJsonAsync<TranscriptionResultDto>();

        transcript.ShouldNotBeNull();
        transcript.Status.ShouldBe("Completed");
        transcript.FullText.ShouldContain("looks very intuitive");
        transcript.Summary.ShouldBe("Prospect confirmed system is intuitive.");
        transcript.Sentiment.ShouldBe("Neutral");
        transcript.ActionItems.ShouldContain("Send follow-up deck");
    }

    [Fact]
    public async Task GDPR_delete_recording_erases_audio_and_flags_deleted()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/call-recording", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create Call and simulate recording
        var callResp = await admin.Client.PostAsJsonAsync("/api/v1/telephony/calls", new InitiateCallRequest(
            ToNumber: "+15559871122",
            Record: true));
        callResp.EnsureSuccessStatusCode();
        var call = await callResp.Content.ReadFromJsonAsync<InitiateCallResult>();
        call.ShouldNotBeNull();

        // Provide recording URL via status callback
        var anonClient = factory.CreateClient();
        await anonClient.PostAsJsonAsync("/api/v1/telephony/webhook/status", new UpdateCallStatusRequest(
            CallSid: call.CallSid,
            Status: "Completed",
            DurationSeconds: 45,
            RecordingUrl: "https://recordings.mindlift.io/audio/call-test-123.mp3"));

        // 2. Read recording before deletion
        var recResp = await admin.Client.GetAsync($"/api/v1/telephony/recording/calls/{call.CallId}");
        recResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var recording = await recResp.Content.ReadFromJsonAsync<CallRecordingDto>();
        recording.ShouldNotBeNull();
        recording.RecordingUrl.ShouldBe("https://recordings.mindlift.io/audio/call-test-123.mp3");
        recording.IsRecordingDeleted.ShouldBeFalse();

        // 3. GDPR erasure
        var deleteResp = await admin.Client.DeleteAsync($"/api/v1/telephony/recording/calls/{call.CallId}");
        deleteResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var deleteResult = await deleteResp.Content.ReadFromJsonAsync<GdprDeleteRecordingResult>();
        deleteResult.ShouldNotBeNull();
        deleteResult.Success.ShouldBeTrue();

        // 4. Verify recording URL is null and marked deleted
        var recAfterResp = await admin.Client.GetAsync($"/api/v1/telephony/recording/calls/{call.CallId}");
        recAfterResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var recordingAfter = await recAfterResp.Content.ReadFromJsonAsync<CallRecordingDto>();
        recordingAfter.ShouldNotBeNull();
        recordingAfter.RecordingUrl.ShouldBeNull();
        recordingAfter.IsRecordingDeleted.ShouldBeTrue();

        // 5. Subsequent transcribe attempt fails due to GDPR erasure
        var transcribeResp = await admin.Client.PostAsJsonAsync($"/api/v1/telephony/recording/calls/{call.CallId}/transcribe", new TranscribeCallRequest());
        transcribeResp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_recording_and_transcription_access()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/call-recording", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/click-to-call", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/call-recording", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates a call and transcribes it
        var callResp = await tenantA.Client.PostAsJsonAsync("/api/v1/telephony/calls", new InitiateCallRequest(
            ToNumber: "+15550001111",
            Record: true));
        callResp.EnsureSuccessStatusCode();
        var call = await callResp.Content.ReadFromJsonAsync<InitiateCallResult>();
        call.ShouldNotBeNull();

        await tenantA.Client.PostAsJsonAsync($"/api/v1/telephony/recording/calls/{call.CallId}/transcribe", new TranscribeCallRequest());

        // Tenant B tries to access Tenant A's call recording
        var crossGetRecording = await tenantB.Client.GetAsync($"/api/v1/telephony/recording/calls/{call.CallId}");
        crossGetRecording.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to access Tenant A's transcription
        var crossGetTranscript = await tenantB.Client.GetAsync($"/api/v1/telephony/recording/calls/{call.CallId}/transcription");
        crossGetTranscript.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to transcribe Tenant A's call
        var crossTranscribe = await tenantB.Client.PostAsJsonAsync($"/api/v1/telephony/recording/calls/{call.CallId}/transcribe", new TranscribeCallRequest());
        crossTranscribe.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to delete Tenant A's recording
        var crossDelete = await tenantB.Client.DeleteAsync($"/api/v1/telephony/recording/calls/{call.CallId}");
        crossDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

internal static class HttpClientTestExtensions
{
    public static Task<HttpResponseMessage> HttpPostAsync(this HttpClient client, string requestUri, HttpContent? content)
        => client.PostAsync(requestUri, content);
}
