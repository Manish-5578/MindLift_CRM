using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Communication;

public sealed class WhatsAppTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveContactRequest NewContact(string first, string last, string email, string phone)
        => new(first, last, email, phone, "Chief Procurement Officer", null, null, null, null, null);

    [Fact]
    public async Task Feature_flag_gates_whatsapp_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Feature is OFF by default
        var respOff = await admin.Client.GetAsync("/api/v1/whatsapp/settings");
        respOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Turn feature ON
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/whatsapp", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // Feature endpoint now succeeds
        var respOn = await admin.Client.GetAsync("/api/v1/whatsapp/settings");
        respOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_and_read_whatsapp_settings()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/whatsapp", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Read default settings
        var settingsResp = await admin.Client.GetAsync("/api/v1/whatsapp/settings");
        settingsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = await settingsResp.Content.ReadFromJsonAsync<WhatsAppSettingsDto>();

        settings.ShouldNotBeNull();
        settings.IsActive.ShouldBeTrue();
        settings.Enforce24HourWindow.ShouldBeTrue();
        settings.WebhookVerifyToken.ShouldNotBeNullOrWhiteSpace();

        // 2. Update settings
        var updateResp = await admin.Client.PutAsJsonAsync("/api/v1/whatsapp/settings", new UpdateWhatsAppSettingsRequest(
            PhoneNumberId: "10987654321",
            WabaId: "98765432109",
            DisplayPhoneNumber: "+15558889999",
            AccessToken: "EAAG_TEST_TOKEN_SECRET",
            WebhookVerifyToken: "my_custom_wa_verify_token_123",
            IsActive: true,
            Enforce24HourWindow: true,
            AutoCreateLeads: true));
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<WhatsAppSettingsDto>();

        updated.ShouldNotBeNull();
        updated.PhoneNumberId.ShouldBe("10987654321");
        updated.WabaId.ShouldBe("98765432109");
        updated.DisplayPhoneNumber.ShouldBe("+15558889999");
        updated.WebhookVerifyToken.ShouldBe("my_custom_wa_verify_token_123");
        updated.AutoCreateLeads.ShouldBeTrue();
    }

    [Fact]
    public async Task Meta_webhook_verification_handshake()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/whatsapp", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var token = "wa_handshake_secret_" + Guid.NewGuid().ToString("N");
        await admin.Client.PutAsJsonAsync("/api/v1/whatsapp/settings", new UpdateWhatsAppSettingsRequest(
            PhoneNumberId: "111222333",
            WabaId: "444555666",
            DisplayPhoneNumber: "+15551112222",
            AccessToken: "token123",
            WebhookVerifyToken: token,
            IsActive: true,
            Enforce24HourWindow: true,
            AutoCreateLeads: false));

        var anonClient = factory.CreateClient();

        // Valid verification request
        var validResp = await anonClient.GetAsync($"/api/v1/whatsapp/webhook?hub.mode=subscribe&hub.verify_token={token}&hub.challenge=challenge_token_999");
        validResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var challengeResult = await validResp.Content.ReadAsStringAsync();
        challengeResult.ShouldBe("challenge_token_999");

        // Invalid token returns 403
        var invalidResp = await anonClient.GetAsync("/api/v1/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=wrong_token&hub.challenge=test");
        invalidResp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Inbound_whatsapp_message_links_contact_and_updates_conversation()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/whatsapp", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var phoneId = "phone_id_" + Guid.NewGuid().ToString("N");
        await admin.Client.PutAsJsonAsync("/api/v1/whatsapp/settings", new UpdateWhatsAppSettingsRequest(
            PhoneNumberId: phoneId,
            WabaId: "waba_id_123",
            DisplayPhoneNumber: "+15550001111",
            AccessToken: "token123",
            WebhookVerifyToken: "token_abc",
            IsActive: true,
            Enforce24HourWindow: true,
            AutoCreateLeads: false));

        // Create contact
        var contactPhone = "+15554447788";
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Grace", "Hopper", "grace@navy.mil", contactPhone));
        contactResp.EnsureSuccessStatusCode();
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();

        // Inbound message arrives via Meta Cloud API webhook
        var payload = new WhatsAppCloudWebhookPayload
        {
            Object = "whatsapp_business_account",
            Entry =
            [
                new WhatsAppEntry
                {
                    Id = "waba_id_123",
                    Changes =
                    [
                        new WhatsAppChange
                        {
                            Field = "messages",
                            Value = new WhatsAppValue
                            {
                                MessagingProduct = "whatsapp",
                                Metadata = new WhatsAppMetadata
                                {
                                    PhoneNumberId = phoneId,
                                    DisplayPhoneNumber = "+15550001111"
                                },
                                Messages =
                                [
                                    new WhatsAppIncomingMessage
                                    {
                                        From = contactPhone,
                                        Id = "wamid.inbound_001",
                                        Type = "text",
                                        Text = new WhatsAppTextContent
                                        {
                                            Body = "Hello MindLift! We want to purchase 50 enterprise licenses."
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            ]
        };

        var anonClient = factory.CreateClient();
        var webhookResp = await anonClient.PostAsJsonAsync("/api/v1/whatsapp/webhook", payload);
        webhookResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify conversation is tracked and linked to contact
        var convResp = await admin.Client.GetAsync("/api/v1/whatsapp/conversations");
        convResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var convs = await convResp.Content.ReadFromJsonAsync<List<WhatsAppConversationDto>>();

        convs.ShouldNotBeNull();
        convs.Count.ShouldBeGreaterThanOrEqualTo(1);
        var conv = convs.FirstOrDefault(c => c.CustomerPhoneNumber == contactPhone);
        conv.ShouldNotBeNull();
        conv.ContactId.ShouldBe(contact.Id);
        conv.Is24HourWindowOpen.ShouldBeTrue();
        conv.UnreadCount.ShouldBeGreaterThanOrEqualTo(1);

        // Verify message is saved
        var msgsResp = await admin.Client.GetAsync($"/api/v1/whatsapp/conversations/{contactPhone}/messages");
        msgsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var msgs = await msgsResp.Content.ReadFromJsonAsync<List<WhatsAppMessageDto>>();

        msgs.ShouldNotBeNull();
        msgs.ShouldContain(m => m.MessageId == "wamid.inbound_001" && m.Content.Contains("50 enterprise licenses") && m.ContactId == contact.Id);
    }

    [Fact]
    public async Task Outbound_messaging_enforces_24_hour_window_for_freeform_and_permits_templates()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/whatsapp", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var phoneId = "phone_id_window_" + Guid.NewGuid().ToString("N");
        await admin.Client.PutAsJsonAsync("/api/v1/whatsapp/settings", new UpdateWhatsAppSettingsRequest(
            PhoneNumberId: phoneId,
            WabaId: "waba_id_window",
            DisplayPhoneNumber: "+15553334444",
            AccessToken: "token123",
            WebhookVerifyToken: "token_win",
            IsActive: true,
            Enforce24HourWindow: true,
            AutoCreateLeads: false));

        var customerPhone = "+15559998877";

        // 1. Attempt freeform message outside 24-hour window -> 400 Bad Request (window_expired)
        var freeformBlockedResp = await admin.Client.PostAsJsonAsync("/api/v1/whatsapp/messages/send", new SendWhatsAppMessageRequest(
            ToNumber: customerPhone,
            MessageType: "text",
            Content: "Hey there! How is everything going?"));
        freeformBlockedResp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 2. Outbound template message succeeds outside 24-hour window
        var templateResp = await admin.Client.PostAsJsonAsync("/api/v1/whatsapp/messages/send", new SendWhatsAppMessageRequest(
            ToNumber: customerPhone,
            MessageType: "template",
            TemplateName: "lead_followup_v1",
            TemplateParameters: ["John", "MindLift Support"]));
        templateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sentTemplate = await templateResp.Content.ReadFromJsonAsync<WhatsAppMessageDto>();
        sentTemplate.ShouldNotBeNull();
        sentTemplate.MessageType.ShouldBe("template");
        sentTemplate.TemplateName.ShouldBe("lead_followup_v1");

        // 3. Customer responds via webhook -> opens 24-hour window
        var anonClient = factory.CreateClient();
        await anonClient.PostAsJsonAsync("/api/v1/whatsapp/webhook", new WhatsAppCloudWebhookPayload
        {
            Object = "whatsapp_business_account",
            Entry =
            [
                new WhatsAppEntry
                {
                    Id = "waba_id_window",
                    Changes =
                    [
                        new WhatsAppChange
                        {
                            Field = "messages",
                            Value = new WhatsAppValue
                            {
                                Metadata = new WhatsAppMetadata { PhoneNumberId = phoneId },
                                Messages =
                                [
                                    new WhatsAppIncomingMessage
                                    {
                                        From = customerPhone,
                                        Id = "wamid.inbound_window_open",
                                        Type = "text",
                                        Text = new WhatsAppTextContent { Body = "Yes, please send more info!" }
                                    }
                                ]
                            }
                        }
                    ]
                }
            ]
        });

        // 4. Now freeform message succeeds because 24-hour window is open
        var freeformAllowedResp = await admin.Client.PostAsJsonAsync("/api/v1/whatsapp/messages/send", new SendWhatsAppMessageRequest(
            ToNumber: customerPhone,
            MessageType: "text",
            Content: "Here is our product overview and demo link: https://crm.example.com"));
        freeformAllowedResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sentFreeform = await freeformAllowedResp.Content.ReadFromJsonAsync<WhatsAppMessageDto>();
        sentFreeform.ShouldNotBeNull();
        sentFreeform.Content.ShouldContain("Here is our product overview");
        sentFreeform.Direction.ShouldBe("Outbound");
    }

    [Fact]
    public async Task Inbound_delivery_and_read_status_callbacks_update_message()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/whatsapp", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var phoneId = "phone_id_status_" + Guid.NewGuid().ToString("N");
        await admin.Client.PutAsJsonAsync("/api/v1/whatsapp/settings", new UpdateWhatsAppSettingsRequest(
            PhoneNumberId: phoneId,
            WabaId: "waba_id_status",
            DisplayPhoneNumber: "+15555556666",
            AccessToken: "token123",
            WebhookVerifyToken: "token_status",
            IsActive: true,
            Enforce24HourWindow: false, // disable window to test status callbacks directly
            AutoCreateLeads: false));

        var targetPhone = "+15551233211";

        // Send outbound message
        var sendResp = await admin.Client.PostAsJsonAsync("/api/v1/whatsapp/messages/send", new SendWhatsAppMessageRequest(
            ToNumber: targetPhone,
            MessageType: "text",
            Content: "Hello from MindLift status tracking test"));
        sendResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sentMsg = await sendResp.Content.ReadFromJsonAsync<WhatsAppMessageDto>();
        sentMsg.ShouldNotBeNull();
        sentMsg.Status.ShouldBe("sent");

        // Webhook receives 'delivered' status
        var anonClient = factory.CreateClient();
        await anonClient.PostAsJsonAsync("/api/v1/whatsapp/webhook", new WhatsAppCloudWebhookPayload
        {
            Object = "whatsapp_business_account",
            Entry =
            [
                new WhatsAppEntry
                {
                    Id = "waba_id_status",
                    Changes =
                    [
                        new WhatsAppChange
                        {
                            Field = "messages",
                            Value = new WhatsAppValue
                            {
                                Metadata = new WhatsAppMetadata { PhoneNumberId = phoneId },
                                Statuses =
                                [
                                    new WhatsAppStatusUpdate
                                    {
                                        Id = sentMsg.MessageId,
                                        Status = "delivered",
                                        RecipientId = targetPhone
                                    }
                                ]
                            }
                        }
                    ]
                }
            ]
        });

        // Webhook receives 'read' status
        await anonClient.PostAsJsonAsync("/api/v1/whatsapp/webhook", new WhatsAppCloudWebhookPayload
        {
            Object = "whatsapp_business_account",
            Entry =
            [
                new WhatsAppEntry
                {
                    Id = "waba_id_status",
                    Changes =
                    [
                        new WhatsAppChange
                        {
                            Field = "messages",
                            Value = new WhatsAppValue
                            {
                                Metadata = new WhatsAppMetadata { PhoneNumberId = phoneId },
                                Statuses =
                                [
                                    new WhatsAppStatusUpdate
                                    {
                                        Id = sentMsg.MessageId,
                                        Status = "read",
                                        RecipientId = targetPhone
                                    }
                                ]
                            }
                        }
                    ]
                }
            ]
        });

        // Verify message status is 'read' and timestamps populated
        var msgsResp = await admin.Client.GetAsync($"/api/v1/whatsapp/conversations/{targetPhone}/messages");
        msgsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var msgs = await msgsResp.Content.ReadFromJsonAsync<List<WhatsAppMessageDto>>();

        msgs.ShouldNotBeNull();
        var tracked = msgs.FirstOrDefault(m => m.MessageId == sentMsg.MessageId);
        tracked.ShouldNotBeNull();
        tracked.Status.ShouldBe("read");
        tracked.DeliveredAtUtc.ShouldNotBeNull();
        tracked.ReadAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_access_to_conversations_and_messages()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/whatsapp", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/whatsapp", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        await tenantA.Client.PutAsJsonAsync("/api/v1/whatsapp/settings", new UpdateWhatsAppSettingsRequest(
            PhoneNumberId: "phone_A_" + Guid.NewGuid().ToString("N"),
            WabaId: "waba_A",
            DisplayPhoneNumber: "+15551110000",
            AccessToken: "tokenA",
            WebhookVerifyToken: "tokenA",
            IsActive: true,
            Enforce24HourWindow: false,
            AutoCreateLeads: false));

        var customerPhone = "+15557778899";

        // Tenant A sends message
        var sendResp = await tenantA.Client.PostAsJsonAsync("/api/v1/whatsapp/messages/send", new SendWhatsAppMessageRequest(
            ToNumber: customerPhone,
            MessageType: "text",
            Content: "Confidential Tenant A communication"));
        sendResp.EnsureSuccessStatusCode();

        // Tenant B queries conversations -> should not see Tenant A's customer
        var convsB = await tenantB.Client.GetFromJsonAsync<List<WhatsAppConversationDto>>("/api/v1/whatsapp/conversations");
        convsB.ShouldNotBeNull();
        convsB.ShouldNotContain(c => c.CustomerPhoneNumber == customerPhone);

        // Tenant B queries messages for that customer -> should be empty
        var msgsB = await tenantB.Client.GetFromJsonAsync<List<WhatsAppMessageDto>>($"/api/v1/whatsapp/conversations/{customerPhone}/messages");
        msgsB.ShouldNotBeNull();
        msgsB.ShouldBeEmpty();
    }
}
