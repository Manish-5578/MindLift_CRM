using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MindLift.IntegrationTests.Support;

/// <summary>Builds provider webhook requests signed the way Meta and Twilio sign them.</summary>
public static class WebhookRequests
{
    /// <summary>A Meta webhook. With a secret the body carries the <c>X-Hub-Signature-256</c> HMAC; without one it is sent unsigned.</summary>
    public static HttpRequestMessage WhatsApp(object payload, string? secret)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/whatsapp") { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (secret is not null)
        {
            request.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes)).ToLowerInvariant());
        }

        return request;
    }

    /// <summary>An inbound WhatsApp text message for the channel with this phone number ID, addressed to this display number.</summary>
    public static object InboundWhatsApp(string phoneNumberId, string displayNumber, string sender, string messageId, string text) => new
    {
        entry = new[]
        {
            new
            {
                changes = new[]
                {
                    new
                    {
                        value = new
                        {
                            metadata = new { phone_number_id = phoneNumberId, display_phone_number = displayNumber },
                            messages = new[] { new { from = sender, id = messageId, text = new { body = text }, type = "text" } },
                        },
                    },
                },
            },
        },
    };

    /// <summary>A Twilio webhook. Twilio signs the full URL followed by every form key and value in key order, with HMAC-SHA1 and the auth token.</summary>
    public static HttpRequestMessage Twilio(string url, IReadOnlyDictionary<string, string> form, string? authToken, IReadOnlyDictionary<string, string>? sentInstead = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/twilio") { Content = new FormUrlEncodedContent(sentInstead ?? form) };
        if (authToken is not null)
        {
            var data = new StringBuilder(url);
            foreach (var pair in form.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                data.Append(pair.Key).Append(pair.Value);
            }

            request.Headers.Add("X-Twilio-Signature", Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(data.ToString()))));
        }

        return request;
    }
}
