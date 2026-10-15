using System.Security.Cryptography;
using System.Text;
using MindLift.Infrastructure.Communication;
using Shouldly;

namespace MindLift.UnitTests.Communication;

/// <summary>The signature checks behind the provider webhooks. The values are computed here the way Meta and Twilio compute them.</summary>
public sealed class WebhookSignatureTests
{
    private static string MetaSignature(byte[] body, string secret)
        => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    [Fact]
    public void A_meta_signature_made_with_the_secret_is_accepted()
    {
        var body = Encoding.UTF8.GetBytes("""{"entry":[]}""");

        WebhookAuthenticator.VerifyMetaSignature(body, MetaSignature(body, "app-secret"), "app-secret").ShouldBeTrue();
    }

    [Fact]
    public void A_meta_signature_is_refused_when_the_secret_body_or_format_is_wrong()
    {
        var body = Encoding.UTF8.GetBytes("""{"entry":[]}""");
        var signature = MetaSignature(body, "app-secret");

        WebhookAuthenticator.VerifyMetaSignature(body, MetaSignature(body, "other-secret"), "app-secret").ShouldBeFalse();
        WebhookAuthenticator.VerifyMetaSignature(Encoding.UTF8.GetBytes("""{"entry":[1]}"""), signature, "app-secret").ShouldBeFalse();
        WebhookAuthenticator.VerifyMetaSignature(body, signature["sha256=".Length..], "app-secret").ShouldBeFalse();
        WebhookAuthenticator.VerifyMetaSignature(body, "sha256=", "app-secret").ShouldBeFalse();
        WebhookAuthenticator.VerifyMetaSignature(body, null, "app-secret").ShouldBeFalse();
        WebhookAuthenticator.VerifyMetaSignature(body, string.Empty, "app-secret").ShouldBeFalse();
    }

    [Fact]
    public void A_twilio_signature_covers_the_url_and_the_form_values_in_key_order()
    {
        var form = new Dictionary<string, string> { ["To"] = "+15550100", ["From"] = "+15550101", ["Body"] = "Hello", ["AccountSid"] = "ACtest" };
        const string url = "https://crm.example.test/api/v1/webhooks/twilio";
        var data = url + "AccountSid" + "ACtest" + "Body" + "Hello" + "From" + "+15550101" + "To" + "+15550100";
        var signature = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes("token"), Encoding.UTF8.GetBytes(data)));

        WebhookAuthenticator.VerifyTwilioSignature(url, form, signature, "token").ShouldBeTrue();
    }

    [Fact]
    public void A_twilio_signature_is_refused_when_anything_it_covers_changes()
    {
        var form = new Dictionary<string, string> { ["AccountSid"] = "ACtest", ["Body"] = "Hello" };
        const string url = "https://crm.example.test/api/v1/webhooks/twilio";
        var data = url + "AccountSid" + "ACtest" + "Body" + "Hello";
        var signature = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes("token"), Encoding.UTF8.GetBytes(data)));

        WebhookAuthenticator.VerifyTwilioSignature(url, new Dictionary<string, string>(form) { ["Body"] = "Changed" }, signature, "token").ShouldBeFalse();
        WebhookAuthenticator.VerifyTwilioSignature("https://elsewhere.example.test/api/v1/webhooks/twilio", form, signature, "token").ShouldBeFalse();
        WebhookAuthenticator.VerifyTwilioSignature(url, form, signature, "wrong-token").ShouldBeFalse();
        WebhookAuthenticator.VerifyTwilioSignature(url, form, "not-a-signature", "token").ShouldBeFalse();
    }
}
