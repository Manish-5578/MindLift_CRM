using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Communication;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/communication")]
public sealed class CommunicationController(
    ICommunicationService communication,
    ITenantContext tenant) : ControllerBase
{
    [HasPermission(AppModule.Communication, PermissionAction.Create)]
    [HttpPost("send")]
    public async Task<ActionResult<MessageDto>> Send([FromBody] SendMessageRequest request, CancellationToken ct)
    {
        var result = await communication.SendMessageAsync(request, tenant.UserId ?? Guid.Empty, ct);
        return Ok(result);
    }

    [HasPermission(AppModule.Communication, PermissionAction.View)]
    [HttpGet("threads/{threadId:guid}")]
    public async Task<ActionResult<List<MessageDto>>> GetThread(Guid threadId, CancellationToken ct)
    {
        return Ok(await communication.GetThreadMessagesAsync(threadId, ct));
    }

    [HasPermission(AppModule.Communication, PermissionAction.Edit)]
    [HttpPost("threads/{threadId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid threadId, CancellationToken ct)
    {
        await communication.MarkThreadReadAsync(threadId, ct);
        return NoContent();
    }

    [HasPermission(AppModule.Communication, PermissionAction.View)]
    [HttpGet("inbox")]
    public async Task<ActionResult<List<InboxThreadDto>>> GetInbox(
        [FromQuery] CommunicationChannel? channel = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        return Ok(await communication.GetInboxThreadsAsync(channel, search, ct));
    }

    [HasPermission(AppModule.Communication, PermissionAction.View)]
    [HttpGet("records/{entityType}/{entityId:guid}")]
    public async Task<ActionResult<List<MessageDto>>> GetRecordMessages(string entityType, Guid entityId, CancellationToken ct)
    {
        return Ok(await communication.GetRecordMessagesAsync(entityType, entityId, ct));
    }
}

[ApiController]
[Route("api/v1/channels")]
public sealed class ChannelAccountsController(IChannelAccountService accounts) : ControllerBase
{
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<List<ChannelAccountDto>>> GetAll(CancellationToken ct)
        => Ok(await accounts.GetAccountsAsync(ct));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChannelAccountDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await accounts.GetAccountByIdAsync(id, ct));

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<ChannelAccountDto>> Create([FromBody] SaveChannelAccountRequest request, CancellationToken ct)
        => Ok(await accounts.SaveAccountAsync(null, request, ct));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ChannelAccountDto>> Update(Guid id, [FromBody] SaveChannelAccountRequest request, CancellationToken ct)
        => Ok(await accounts.SaveAccountAsync(id, request, ct));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await accounts.DeleteAccountAsync(id, ct);
        return NoContent();
    }

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<TestConnectionResult>> TestConnection(Guid id, CancellationToken ct)
        => Ok(await accounts.TestConnectionAsync(id, ct));
}

[ApiController]
[Route("api/v1/message-templates")]
public sealed class MessageTemplatesController(IMessageTemplateService templates) : ControllerBase
{
    [HasPermission(AppModule.Communication, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<List<MessageTemplateDto>>> GetAll([FromQuery] CommunicationChannel? channel = null, CancellationToken ct = default)
        => Ok(await templates.GetTemplatesAsync(channel, ct));

    [HasPermission(AppModule.Communication, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MessageTemplateDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await templates.GetTemplateByIdAsync(id, ct));

    [HasPermission(AppModule.Communication, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<MessageTemplateDto>> Create([FromBody] SaveMessageTemplateRequest request, CancellationToken ct)
        => Ok(await templates.SaveTemplateAsync(null, request, ct));

    [HasPermission(AppModule.Communication, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MessageTemplateDto>> Update(Guid id, [FromBody] SaveMessageTemplateRequest request, CancellationToken ct)
        => Ok(await templates.SaveTemplateAsync(id, request, ct));

    [HasPermission(AppModule.Communication, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await templates.DeleteTemplateAsync(id, ct);
        return NoContent();
    }

    public sealed record RenderTemplateRequest(string EntityType, Guid EntityId, Dictionary<string, string>? ExtraParams = null);

    [HasPermission(AppModule.Communication, PermissionAction.View)]
    [HttpPost("{id:guid}/render")]
    public async Task<ActionResult<string>> Render(Guid id, [FromBody] RenderTemplateRequest req, CancellationToken ct)
        => Ok(await templates.RenderTemplateAsync(id, req.EntityType, req.EntityId, req.ExtraParams, ct));
}

[ApiController]
[Route("api/v1/opt-outs")]
public sealed class OptOutsController(IOptOutService optOuts) : ControllerBase
{
    [HasPermission(AppModule.Communication, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<List<OptOutDto>>> GetAll(CancellationToken ct)
        => Ok(await optOuts.GetOptOutsAsync(ct));

    [HasPermission(AppModule.Communication, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<OptOutDto>> Register([FromBody] RegisterOptOutRequest request, CancellationToken ct)
        => Ok(await optOuts.RegisterOptOutAsync(request, ct));

    [HasPermission(AppModule.Communication, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await optOuts.RemoveOptOutAsync(id, ct);
        return NoContent();
    }
}

/// <summary>
/// Inbound provider webhooks. These are anonymous by nature, so every request must prove it comes from the provider: Meta requests carry an HMAC of the body
/// and Twilio requests carry an HMAC of the URL and form values. A request that cannot be verified is refused before anything is read or stored.
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
[AllowAnonymous]
[EnableRateLimiting("webhook")]
public sealed class WebhooksController(
    ICommunicationService communication,
    WebhookAuthenticator authenticator,
    MindLift.Infrastructure.Tenancy.TenantStanding standing,
    IConfiguration configuration,
    ILogger<WebhooksController> logger) : ControllerBase
{
    /// <summary>Provider payloads are small; anything larger is refused.</summary>
    private const int MaxBodyBytes = 1_000_000;

    [HttpGet("whatsapp")]
    public IActionResult VerifyWhatsApp(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (mode == "subscribe" && !string.IsNullOrEmpty(challenge) && authenticator.VerifyRegistrationToken(token))
        {
            return Content(challenge, "text/plain");
        }

        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost("whatsapp")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> HandleWhatsAppWebhook(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();
        var signature = Request.Headers["X-Hub-Signature-256"].ToString();

        using var doc = TryParse(body);
        if (doc is null)
        {
            return BadRequest();
        }

        // Every change is checked against the channel it names before anything is stored.
        var verified = new List<(JsonElement Value, VerifiedChannel Channel)>();
        foreach (var value in Values(doc.RootElement))
        {
            var metadata = value.TryGetProperty("metadata", out var m) ? m : default;
            var channel = await authenticator.VerifyWhatsAppAsync(Text(metadata, "phone_number_id"), Text(metadata, "display_phone_number"), body, signature, ct);
            if (channel is null)
            {
                logger.LogWarning("Refused a WhatsApp webhook that could not be verified.");
                return Unauthorized();
            }

            // A workspace that has lapsed takes no new messages. The request is genuine, so it is acknowledged (the provider stops retrying) and nothing is stored.
            if (await standing.IsLapsedAsync(channel.TenantId, ct))
            {
                logger.LogWarning("Ignored a WhatsApp webhook for a workspace that has lapsed.");
                return Ok();
            }

            verified.Add((value, channel));
        }

        foreach (var (value, channel) in verified)
        {
            if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in statuses.EnumerateArray())
                {
                    var id = Text(s, "id");
                    var status = Text(s, "status") switch
                    {
                        "delivered" => MessageStatus.Delivered,
                        "read" => MessageStatus.Read,
                        "failed" => MessageStatus.Failed,
                        _ => MessageStatus.Sent
                    };

                    if (!string.IsNullOrEmpty(id))
                    {
                        await communication.UpdateMessageStatusAsync(id, status, tenantId: channel.TenantId, ct: ct);
                    }
                }
            }

            if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                var toPhone = value.TryGetProperty("metadata", out var metadata) ? Text(metadata, "display_phone_number") ?? string.Empty : string.Empty;
                foreach (var m in messages.EnumerateArray())
                {
                    var from = Text(m, "from") ?? string.Empty;
                    var text = m.TryGetProperty("text", out var t) ? Text(t, "body") ?? string.Empty : "[Media message]";
                    var payload = new InboundMessagePayload(CommunicationChannel.WhatsApp, from, toPhone, null, text, Text(m, "id"), DateTime.UtcNow);
                    await communication.HandleInboundMessageAsync(payload, channel.AccountId, ct);
                }
            }
        }

        return Ok();
    }

    [HttpPost("twilio")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> HandleTwilioWebhook(CancellationToken ct)
    {
        if (!Request.HasFormContentType)
        {
            return BadRequest();
        }

        var form = await Request.ReadFormAsync(ct);
        var values = form.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);
        var channel = await authenticator.VerifyTwilioAsync(PublicUrl(), values, Request.Headers["X-Twilio-Signature"].ToString(), ct);
        if (channel is null)
        {
            logger.LogWarning("Refused a Twilio webhook that could not be verified.");
            return Unauthorized();
        }

        if (await standing.IsLapsedAsync(channel.TenantId, ct))
        {
            logger.LogWarning("Ignored a Twilio webhook for a workspace that has lapsed.");
            return Ok();
        }

        var messageSid = form["MessageSid"].ToString();
        var smsStatus = form["SmsStatus"].ToString();

        if (!string.IsNullOrEmpty(messageSid) && !string.IsNullOrEmpty(smsStatus))
        {
            var status = smsStatus.ToLowerInvariant() switch
            {
                "delivered" => MessageStatus.Delivered,
                "sent" => MessageStatus.Sent,
                "failed" => MessageStatus.Failed,
                "undelivered" => MessageStatus.Failed,
                _ => MessageStatus.Sent
            };

            await communication.UpdateMessageStatusAsync(messageSid, status, tenantId: channel.TenantId, ct: ct);
            return Ok();
        }

        // Inbound SMS
        var from = form["From"].ToString();
        var to = form["To"].ToString();
        var body = form["Body"].ToString();

        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(body))
        {
            var payload = new InboundMessagePayload(CommunicationChannel.Sms, from, to, null, body, messageSid, DateTime.UtcNow);
            await communication.HandleInboundMessageAsync(payload, channel.AccountId, ct);
        }

        return Ok();
    }

    /// <summary>The address the provider called, as it was configured with them. Twilio signs this exact text, so a proxy must not change it.</summary>
    private string PublicUrl()
    {
        var baseUrl = configuration["Communication:PublicBaseUrl"] ?? configuration["App:BaseUrl"];
        var path = Request.Path + Request.QueryString.ToString();
        return !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.TrimEnd('/') + path : $"{Request.Scheme}://{Request.Host}{path}";
    }

    private static JsonDocument? TryParse(byte[] body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Every <c>value</c> object under <c>entry[].changes[]</c>, skipping anything that is not shaped that way.</summary>
    private static IEnumerable<JsonElement> Values(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (change.ValueKind == JsonValueKind.Object && change.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object)
                {
                    yield return value;
                }
            }
        }
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
