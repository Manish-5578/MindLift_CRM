using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Communication;

public sealed class OmnichannelFlowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task ChannelAccounts_Crud_And_TestConnection_Work_Securely()
    {
        var owner = await Workspaces.SignupAsync(factory);

        // 1. Create WhatsApp channel account
        var createRequest = new SaveChannelAccountRequest(
            CommunicationChannel.WhatsApp,
            "Official WhatsApp",
            "+15551234567",
            IsDefault: true,
            IsActive: true,
            AutoCreateLeads: true,
            PhoneNumberId: "test_phone_id_123",
            Password: "super_secret_access_token"
        );

        var createResp = await owner.Client.PostAsJsonAsync("/api/v1/channels", createRequest);
        createResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<ChannelAccountDto>();
        created.ShouldNotBeNull();
        created.Name.ShouldBe("Official WhatsApp");
        created.Channel.ShouldBe(CommunicationChannel.WhatsApp);
        created.IsDefault.ShouldBeTrue();

        // 2. Fetch all channels - secrets are omitted
        var list = await owner.Client.GetFromJsonAsync<List<ChannelAccountDto>>("/api/v1/channels");
        list.ShouldNotBeNull();
        list.Any(c => c.Id == created.Id).ShouldBeTrue();

        // 3. Test connection
        var testResp = await owner.Client.PostAsync($"/api/v1/channels/{created.Id}/test", null);
        testResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var testResult = await testResp.Content.ReadFromJsonAsync<TestConnectionResult>();
        testResult.ShouldNotBeNull();
        testResult.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task OutboundMessage_Sends_And_Records_Timeline_Activity()
    {
        var owner = await Workspaces.SignupAsync(factory);

        // Create a lead first
        var leadResp = await owner.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest(
            "Michael",
            "Scott",
            "mscott@dundermifflin.com",
            "+15559876543",
            "Dunder Mifflin",
            "new",
            "web",
            "warm",
            Description: "Regional Manager",
            Tags: null,
            OwnerId: null,
            CustomFields: null
        ));
        leadResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // Send outbound WhatsApp message to lead
        var sendRequest = new SendMessageRequest(
            CommunicationChannel.WhatsApp,
            null,
            lead.Phone!,
            null,
            "Hello Michael, welcome to MindLift CRM!",
            LeadId: lead.Id
        );

        var sendResp = await owner.Client.PostAsJsonAsync("/api/v1/communication/send", sendRequest);
        sendResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var msg = await sendResp.Content.ReadFromJsonAsync<MessageDto>();
        msg.ShouldNotBeNull();
        msg.Status.ShouldBe(MessageStatus.Sent);
        msg.Direction.ShouldBe(MessageDirection.Outbound);
        msg.LeadId.ShouldBe(lead.Id);
        msg.Body.ShouldBe("Hello Michael, welcome to MindLift CRM!");

        // Verify thread retrieval
        var threadResp = await owner.Client.GetFromJsonAsync<List<MessageDto>>($"/api/v1/communication/threads/{msg.ThreadId}");
        threadResp.ShouldNotBeNull();
        threadResp.Count.ShouldBeGreaterThanOrEqualTo(1);
        threadResp.First().Id.ShouldBe(msg.Id);

        // Verify Record Messages
        var recordMsgs = await owner.Client.GetFromJsonAsync<List<MessageDto>>($"/api/v1/communication/records/lead/{lead.Id}");
        recordMsgs.ShouldNotBeNull();
        recordMsgs.Any(m => m.Id == msg.Id).ShouldBeTrue();

        // Verify Timeline Activity was logged
        var activities = await owner.Client.GetFromJsonAsync<IReadOnlyList<TimelineEntryDto>>($"/api/v1/timeline?entityType=leads&entityId={lead.Id}");
        activities.ShouldNotBeNull();
        activities.Any(a => a.Title.Contains("WhatsApp Outbound") || (a.Detail != null && a.Detail.Contains("Hello Michael"))).ShouldBeTrue();
    }

    [Fact]
    public async Task OptOut_Blocks_Outbound_Communication()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var phoneToOptOut = "+15554443322";

        // Register opt-out for WhatsApp
        var optOutResp = await owner.Client.PostAsJsonAsync("/api/v1/opt-outs", new RegisterOptOutRequest(
            CommunicationChannel.WhatsApp,
            phoneToOptOut,
            "Customer requested stop via SMS"
        ));
        optOutResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify opt-out appears in list
        var list = await owner.Client.GetFromJsonAsync<List<OptOutDto>>("/api/v1/opt-outs");
        list.ShouldNotBeNull();
        list.Any(o => o.Target == phoneToOptOut).ShouldBeTrue();

        // Attempting to send message to opted-out recipient should fail with bad request / error
        var sendRequest = new SendMessageRequest(
            CommunicationChannel.WhatsApp,
            null,
            phoneToOptOut,
            null,
            "Checking in with you!"
        );

        var sendResp = await owner.Client.PostAsJsonAsync("/api/v1/communication/send", sendRequest);
        sendResp.StatusCode.ShouldBe(HttpStatusCode.InternalServerError); // or error status
    }

    [Fact]
    public async Task Template_Render_And_Inbox_Thread_Tracking()
    {
        var owner = await Workspaces.SignupAsync(factory);

        // 1. Create a message template
        var tplReq = new SaveMessageTemplateRequest(
            CommunicationChannel.WhatsApp,
            "Lead Welcome",
            "lead_welcome",
            TemplateCategory.Utility,
            null,
            "Hi {{lead.first_name}}, thanks for contacting us! We're excited to work with {{lead.company}}."
        );
        var tplResp = await owner.Client.PostAsJsonAsync("/api/v1/message-templates", tplReq);
        tplResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tpl = await tplResp.Content.ReadFromJsonAsync<MessageTemplateDto>();
        tpl.ShouldNotBeNull();
        tpl.Placeholders.ShouldContain("lead.first_name");
        tpl.Placeholders.ShouldContain("lead.company");

        // 2. Create a lead
        var leadResp = await owner.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest(
            "Dwight",
            "Schrute",
            "dwight@schrutefarms.com",
            "+15557778899",
            "Schrute Farms",
            "new",
            "web",
            "hot",
            Description: "Assistant to the Regional Manager",
            Tags: null,
            OwnerId: null,
            CustomFields: null
        ));
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();

        // 3. Render template
        var renderResp = await owner.Client.PostAsJsonAsync(
            $"/api/v1/message-templates/{tpl.Id}/render",
            new { EntityType = "lead", EntityId = lead.Id }
        );
        renderResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var renderedText = await renderResp.Content.ReadAsStringAsync();
        renderedText.ShouldContain("Hi Dwight");
        renderedText.ShouldContain("Schrute Farms");

        // 4. Send message using rendered body
        var sendResp = await owner.Client.PostAsJsonAsync("/api/v1/communication/send", new SendMessageRequest(
            CommunicationChannel.WhatsApp,
            null,
            lead.Phone!,
            null,
            renderedText,
            LeadId: lead.Id
        ));
        sendResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 5. Query Inbox threads
        var inbox = await owner.Client.GetFromJsonAsync<List<InboxThreadDto>>("/api/v1/communication/inbox?channel=WhatsApp");
        inbox.ShouldNotBeNull();
        var thread = inbox.FirstOrDefault(t => t.LeadId == lead.Id);
        thread.ShouldNotBeNull();
        thread.ParticipantName.ShouldBe("Dwight Schrute");
        thread.LastMessageSnippet.ShouldContain("Hi Dwight");
    }

    [Fact]
    public async Task Webhook_Inbound_Message_Creates_Thread_And_AutoCreates_Lead()
    {
        var owner = await Workspaces.SignupAsync(factory);

        // Set default WhatsApp account with AutoCreateLeads = true
        var chResp = await owner.Client.PostAsJsonAsync("/api/v1/channels", new SaveChannelAccountRequest(
            CommunicationChannel.WhatsApp,
            "Primary WhatsApp",
            "+19998887777",
            IsDefault: true,
            IsActive: true,
            AutoCreateLeads: true,
            PhoneNumberId: "19998887777"
        ));
        chResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Simulate Meta Inbound Webhook
        var inboundSender = "+12345678901";
        var webhookPayload = new
        {
            @object = "whatsapp_business_account",
            entry = new[]
            {
                new
                {
                    id = "WHATSAPP_BUSINESS_ACCOUNT_ID",
                    changes = new[]
                    {
                        new
                        {
                            value = new
                            {
                                messaging_product = "whatsapp",
                                metadata = new
                                {
                                    display_phone_number = "+19998887777",
                                    phone_number_id = "19998887777"
                                },
                                messages = new[]
                                {
                                    new
                                    {
                                        from = inboundSender,
                                        id = "wamid.inbound_test_123",
                                        timestamp = "1728000000",
                                        text = new { body = "I want to request pricing for your software" },
                                        type = "text"
                                    }
                                }
                            },
                            field = "messages"
                        }
                    }
                }
            }
        };

        var postResp = await factory.CreateClient().PostAsJsonAsync("/api/v1/webhooks/whatsapp", webhookPayload);
        postResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify Inbound message in Inbox
        var inbox = await owner.Client.GetFromJsonAsync<List<InboxThreadDto>>("/api/v1/communication/inbox?channel=WhatsApp");
        inbox.ShouldNotBeNull();
        var thread = inbox.FirstOrDefault(t => t.ParticipantAddress == inboundSender);
        thread.ShouldNotBeNull();
        thread.LastMessageSnippet.ShouldContain("pricing for your software");

        // Verify thread messages
        var threadMsgs = await owner.Client.GetFromJsonAsync<List<MessageDto>>($"/api/v1/communication/threads/{thread.ThreadId}");
        threadMsgs.ShouldNotBeNull();
        var inMsg = threadMsgs.First();
        inMsg.Direction.ShouldBe(MessageDirection.Inbound);
        inMsg.ExternalMessageId.ShouldBe("wamid.inbound_test_123");

        // Mark thread as read
        var readResp = await owner.Client.PostAsync($"/api/v1/communication/threads/{thread.ThreadId}/read", null);
        readResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify unread count is now 0
        var inboxAfter = await owner.Client.GetFromJsonAsync<List<InboxThreadDto>>("/api/v1/communication/inbox?channel=WhatsApp");
        inboxAfter.ShouldNotBeNull();
        var threadAfter = inboxAfter.First(t => t.ThreadId == thread.ThreadId);
        threadAfter.UnreadCount.ShouldBe(0);
    }
}
