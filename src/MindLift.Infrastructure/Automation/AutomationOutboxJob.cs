using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;

namespace MindLift.Infrastructure.Automation;

/// <summary>Sends an automation's mail or webhook after the work that triggered it, when it could not be sent on the spot.</summary>
public sealed class AutomationOutboxJob(IEmailSender email, IHttpClientFactory httpClientFactory, ILogger<AutomationOutboxJob> logger)
{
    /// <summary>Records can point back to each other; the webhook body stops at the first repeat instead of failing.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };

    public Task SendEmailAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
        => email.SendAsync(recipient, subject, body, cancellationToken);

    public async Task PostWebhookAsync(string url, string json, CancellationToken cancellationToken)
    {
        // Checked again here: the address may have changed between queuing and sending.
        if (!WebhookSecurity.IsSafeUrl(url, out var reason))
        {
            logger.LogWarning("Queued webhook to {Url} blocked: {Reason}", url, reason);
            return;
        }

        var client = httpClientFactory.CreateClient("AutomationWebhooks");
        client.Timeout = TimeSpan.FromSeconds(5);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
