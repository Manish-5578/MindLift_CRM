using MindLift.Shared.Enums;
using MindLift.Api.Auth;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Infrastructure.Chat;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/chat")]
[Authorize]
[SignedInUser]
public sealed class ChatController(ChatService chatService) : ControllerBase
{
    [HttpGet("messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetMessages(
        [FromQuery] string? channel = "general",
        [FromQuery] Guid? recipientId = null,
        [FromQuery] DateTime? since = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        return Ok(await chatService.GetMessagesAsync(channel, recipientId, since, ct, search));
    }

    /// <summary>Multipart form: file, and optionally message (caption), recipientId, channel, replyToId, durationMs.</summary>
    [HttpPost("messages/attachment")]
    [RequestSizeLimit(16L * 1024 * 1024)]
    public async Task<ActionResult<ChatMessageDto>> SendAttachment(
        IFormFile? file,
        [FromForm] string? message,
        [FromForm] Guid? recipientId,
        [FromForm] string? channel,
        [FromForm] Guid? replyToId,
        [FromForm] int? durationMs,
        CancellationToken ct = default)
    {
        if (file is null) return BadRequest(new { error = "Choose a file." });
        await using var stream = file.OpenReadStream();
        return Ok(await chatService.SendAttachmentAsync(stream, file.FileName, file.Length, message, channel, recipientId, replyToId, durationMs, ct));
    }

    [HttpGet("messages/{id:guid}/attachment")]
    public async Task<IActionResult> Attachment(Guid id, CancellationToken ct = default)
    {
        var (name, type, content) = await chatService.OpenAttachmentAsync(id, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content, type, name, enableRangeProcessing: true);
    }

    [HttpPost("messages/{id:guid}/react")]
    public async Task<ActionResult<ChatMessageDto>> React(Guid id, [FromBody] ReactRequest request, CancellationToken ct = default)
        => Ok(await chatService.ReactAsync(id, request.Emoji, ct));

    [HttpPost("typing")]
    public async Task<IActionResult> Typing([FromQuery] Guid toUserId)
    {
        await chatService.TypingAsync(toUserId);
        return NoContent();
    }

    [HttpGet("typing")]
    public ActionResult<object> IsTyping([FromQuery] Guid otherUserId)
        => Ok(new { typing = chatService.IsTyping(otherUserId) });

    [HttpPost("messages")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(
        [FromBody] SendChatMessageRequest request,
        CancellationToken ct = default)
    {
        var result = await chatService.SendMessageAsync(request, ct);
        return CreatedAtAction(nameof(GetMessages), new { channel = result.ChannelName, recipientId = result.RecipientId }, result);
    }

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<ChatUserDto>>> GetUsers(CancellationToken ct = default)
    {
        return Ok(await chatService.GetChatUsersAsync(ct));
    }

    [HttpPost("ping")]
    public async Task<IActionResult> Ping()
    {
        await chatService.PingAsync();
        return NoContent();
    }

    [HttpPost("offline")]
    public async Task<IActionResult> Offline()
    {
        await chatService.GoOfflineAsync();
        return NoContent();
    }

    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkRead(
        [FromQuery] Guid? otherUserId = null,
        [FromQuery] string? channel = null,
        CancellationToken ct = default)
    {
        await chatService.MarkReadAsync(otherUserId, channel, ct);
        return NoContent();
    }
}

