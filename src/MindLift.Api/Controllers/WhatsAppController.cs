using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 5 / #34: WhatsApp Business (whatsapp).
/// REST endpoints for Meta WhatsApp Cloud API settings, webhook verification & ingress,
/// template & interactive message dispatch, 24-hour customer care window tracking,
/// and conversation histories.
/// </summary>
[ApiController]
[Route("api/v1/whatsapp")]
public sealed class WhatsAppController(IWhatsAppService whatsAppService) : ControllerBase
{
    // ==========================================
    // Public Meta Cloud API Webhook Endpoints
    // ==========================================

    [AllowAnonymous]
    [HttpGet("webhook")]
    public async Task<IActionResult> VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        CancellationToken ct)
    {
        var result = await whatsAppService.VerifyWebhookAsync(mode, verifyToken, challenge, ct);
        if (result != null)
        {
            return Content(result, "text/plain");
        }

        return Forbid();
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> InboundWebhook(
        [FromBody] WhatsAppCloudWebhookPayload payload,
        CancellationToken ct)
    {
        await whatsAppService.ProcessWebhookPayloadAsync(payload, ct);
        return Ok(new { success = true });
    }

    // ==========================================
    // Authenticated Endpoints (RequiresFeature "whatsapp")
    // ==========================================

    [Authorize]
    [RequiresFeature("whatsapp")]
    [HttpGet("settings")]
    public async Task<ActionResult<WhatsAppSettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await whatsAppService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [Authorize]
    [RequiresFeature("whatsapp")]
    [HttpPut("settings")]
    public async Task<ActionResult<WhatsAppSettingsDto>> UpdateSettings(
        [FromBody] UpdateWhatsAppSettingsRequest request,
        CancellationToken ct)
    {
        var updated = await whatsAppService.UpdateSettingsAsync(request, ct);
        return Ok(updated);
    }

    [Authorize]
    [RequiresFeature("whatsapp")]
    [HttpPost("messages/send")]
    public async Task<ActionResult<WhatsAppMessageDto>> SendMessage(
        [FromBody] SendWhatsAppMessageRequest request,
        CancellationToken ct)
    {
        var sent = await whatsAppService.SendMessageAsync(request, ct);
        return Ok(sent);
    }

    [Authorize]
    [RequiresFeature("whatsapp")]
    [HttpGet("conversations")]
    public async Task<ActionResult<IReadOnlyList<WhatsAppConversationDto>>> GetConversations(CancellationToken ct)
    {
        var convs = await whatsAppService.GetConversationsAsync(ct);
        return Ok(convs);
    }

    [Authorize]
    [RequiresFeature("whatsapp")]
    [HttpGet("conversations/{phone}/messages")]
    public async Task<ActionResult<IReadOnlyList<WhatsAppMessageDto>>> GetMessages(
        string phone,
        CancellationToken ct)
    {
        var messages = await whatsAppService.GetMessagesAsync(phone, ct);
        return Ok(messages);
    }
}
