using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Communication;

/// <summary>Provider webhooks are anonymous, so each one has to prove where it came from before anything is stored.</summary>
public sealed class WebhookSecurityTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static string NewNumber() => "1" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999);

    private static async Task CreateWhatsAppChannelAsync(Session owner, string number, string? secret)
    {
        var response = await owner.Client.PostAsJsonAsync("/api/v1/channels", new SaveChannelAccountRequest(
            CommunicationChannel.WhatsApp, "WhatsApp", "+" + number, IsDefault: true, IsActive: true, AutoCreateLeads: true,
            PhoneNumberId: number, WebhookSecret: secret));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<List<InboxThreadDto>> InboxAsync(Session owner, string channel)
        => (await owner.Client.GetFromJsonAsync<List<InboxThreadDto>>($"/api/v1/communication/inbox?channel={channel}"))!;

    [Fact]
    public async Task A_correctly_signed_message_is_stored()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var number = NewNumber();
        await CreateWhatsAppChannelAsync(owner, number, "secret-one");

        var response = await factory.CreateClient().SendAsync(WebhookRequests.WhatsApp(WebhookRequests.InboundWhatsApp(number, "+" + number, "+15550001", "wamid.ok", "hello"), "secret-one"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await InboxAsync(owner, "WhatsApp")).ShouldContain(t => t.ParticipantAddress == "+15550001");
    }

    [Fact]
    public async Task An_unsigned_message_is_refused_and_nothing_is_stored()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var number = NewNumber();
        await CreateWhatsAppChannelAsync(owner, number, "secret-two");

        var response = await factory.CreateClient().SendAsync(WebhookRequests.WhatsApp(WebhookRequests.InboundWhatsApp(number, "+" + number, "+15550002", "wamid.unsigned", "forged"), secret: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await InboxAsync(owner, "WhatsApp")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_message_signed_with_the_wrong_secret_is_refused()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var number = NewNumber();
        await CreateWhatsAppChannelAsync(owner, number, "the-real-secret");

        var response = await factory.CreateClient().SendAsync(WebhookRequests.WhatsApp(WebhookRequests.InboundWhatsApp(number, "+" + number, "+15550003", "wamid.wrong", "forged"), "a-guessed-secret"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await InboxAsync(owner, "WhatsApp")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_channel_with_no_secret_never_accepts_webhooks()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var number = NewNumber();
        await CreateWhatsAppChannelAsync(owner, number, secret: null);

        var response = await factory.CreateClient().SendAsync(WebhookRequests.WhatsApp(WebhookRequests.InboundWhatsApp(number, "+" + number, "+15550004", "wamid.nosecret", "forged"), ""));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await InboxAsync(owner, "WhatsApp")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_message_for_an_unknown_number_is_refused()
    {
        var number = NewNumber();

        var response = await factory.CreateClient().SendAsync(WebhookRequests.WhatsApp(WebhookRequests.InboundWhatsApp(number, "+" + number, "+15550005", "wamid.unknown", "hello"), "any-secret"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_secret_cannot_deliver_a_message_into_another_workspace()
    {
        var attacker = await Workspaces.SignupAsync(factory);
        var victim = await Workspaces.SignupAsync(factory);
        var attackerNumber = NewNumber();
        var victimNumber = NewNumber();
        await CreateWhatsAppChannelAsync(attacker, attackerNumber, "attacker-secret");
        await CreateWhatsAppChannelAsync(victim, victimNumber, "victim-secret");

        // Signed with the attacker's own secret, naming the attacker's channel, but addressed to the victim's number.
        var response = await factory.CreateClient().SendAsync(WebhookRequests.WhatsApp(
            WebhookRequests.InboundWhatsApp(attackerNumber, "+" + victimNumber, "+15550006", "wamid.cross", "planted"), "attacker-secret"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await InboxAsync(victim, "WhatsApp")).ShouldBeEmpty();
        (await InboxAsync(attacker, "WhatsApp")).ShouldContain(t => t.ParticipantAddress == "+15550006");
    }

    [Fact]
    public async Task Registering_the_webhook_needs_the_configured_token()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=guess&hub.challenge=abc")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/webhooks/whatsapp?hub.mode=subscribe&hub.challenge=abc")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var ok = await client.GetAsync($"/api/v1/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={MindLiftApiFactory.WhatsAppVerifyToken}&hub.challenge=abc123");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).ShouldBe("abc123");
    }

    [Fact]
    public async Task A_signed_sms_is_stored_and_an_unsigned_or_altered_one_is_refused()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var number = NewNumber();
        var accountSid = "AC" + Guid.NewGuid().ToString("N");
        var create = await owner.Client.PostAsJsonAsync("/api/v1/channels", new SaveChannelAccountRequest(
            CommunicationChannel.Sms, "SMS", "+" + number, IsDefault: true, IsActive: true, AutoCreateLeads: true,
            AccountSid: accountSid, AuthToken: "twilio-test-token"));
        create.StatusCode.ShouldBe(HttpStatusCode.OK, await create.Content.ReadAsStringAsync());

        var config = factory.Services.GetRequiredService<IConfiguration>();
        var baseUrl = (config["Communication:PublicBaseUrl"] ?? config["App:BaseUrl"])?.TrimEnd('/') ?? "http://localhost";
        var url = baseUrl + "/api/v1/webhooks/twilio";
        var form = new Dictionary<string, string>
        {
            ["AccountSid"] = accountSid, ["From"] = "+15550007", ["To"] = "+" + number, ["Body"] = "Hi from SMS", ["MessageSid"] = "SM" + Guid.NewGuid().ToString("N"),
        };
        var client = factory.CreateClient();

        (await client.SendAsync(WebhookRequests.Twilio(url, form, authToken: null))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var altered = new Dictionary<string, string>(form) { ["Body"] = "Changed after signing" };
        (await client.SendAsync(WebhookRequests.Twilio(url, form, "twilio-test-token", sentInstead: altered))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await InboxAsync(owner, "Sms")).ShouldBeEmpty();

        (await client.SendAsync(WebhookRequests.Twilio(url, form, "twilio-test-token"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await InboxAsync(owner, "Sms")).ShouldContain(t => t.ParticipantAddress == "+15550007");
    }
}
