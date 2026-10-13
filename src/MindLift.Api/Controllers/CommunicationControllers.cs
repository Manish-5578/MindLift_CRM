using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
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

[ApiController]
[Route("api/v1/webhooks")]
[AllowAnonymous]
public sealed class WebhooksController(
    ICommunicationService communication,
    AppDbContext dbContext,
    ITenantContext tenant,
    ILogger<WebhooksController> logger) : ControllerBase
{
    [HttpGet("whatsapp")]
    public IActionResult VerifyWhatsApp(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (mode == "subscribe" && !string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(challenge))
        {
            return Ok(challenge);
        }
        return Forbid();
    }

    [HttpPost("whatsapp")]
    public async Task<IActionResult> HandleWhatsAppWebhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);

        // Parse JSON
        using var doc = JsonDocument.Parse(rawBody);
        if (!doc.RootElement.TryGetProperty("entry", out var entries))
        {
            return Ok();
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes)) continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var val)) continue;

                // Handle message status updates
                if (val.TryGetProperty("statuses", out var statuses))
                {
                    foreach (var s in statuses.EnumerateArray())
                    {
                        var id = s.GetProperty("id").GetString();
                        var statusStr = s.GetProperty("status").GetString();
                        var status = statusStr switch
                        {
                            "sent" => MessageStatus.Sent,
                            "delivered" => MessageStatus.Delivered,
                            "read" => MessageStatus.Read,
                            "failed" => MessageStatus.Failed,
                            _ => MessageStatus.Sent
                        };

                        if (!string.IsNullOrEmpty(id))
                        {
                            await communication.UpdateMessageStatusAsync(id, status, ct: ct);
                        }
                    }
                }

                // Handle inbound messages
                if (val.TryGetProperty("messages", out var messages))
                {
                    var metadata = val.GetProperty("metadata");
                    var toPhone = metadata.GetProperty("display_phone_number").GetString() ?? "";

                    foreach (var m in messages.EnumerateArray())
                    {
                        var from = m.GetProperty("from").GetString() ?? "";
                        var msgId = m.GetProperty("id").GetString();
                        var text = m.TryGetProperty("text", out var t) && t.TryGetProperty("body", out var b)
                            ? b.GetString() ?? ""
                            : "[Media message]";

                        var payload = new InboundMessagePayload(
                            CommunicationChannel.WhatsApp,
                            from,
                            toPhone,
                            null,
                            text,
                            msgId,
                            DateTime.UtcNow
                        );

                        await communication.HandleInboundMessageAsync(payload, ct);
                    }
                }
            }
        }

        return Ok();
    }

    [HttpPost("twilio")]
    public async Task<IActionResult> HandleTwilioWebhook(CancellationToken ct)
    {
        var form = await Request.ReadFormAsync(ct);
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

            await communication.UpdateMessageStatusAsync(messageSid, status, ct: ct);
            return Ok();
        }

        // Inbound SMS
        var from = form["From"].ToString();
        var to = form["To"].ToString();
        var body = form["Body"].ToString();

        if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(body))
        {
            var payload = new InboundMessagePayload(
                CommunicationChannel.Sms,
                from,
                to,
                null,
                body,
                messageSid,
                DateTime.UtcNow
            );

            await communication.HandleInboundMessageAsync(payload, ct);
        }

        return Ok();
    }
}
