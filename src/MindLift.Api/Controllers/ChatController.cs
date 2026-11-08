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
public sealed class ChatController(ChatService chatService) : ControllerBase
{
    [HttpGet("messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetMessages(
        [FromQuery] string? channel = "general",
        [FromQuery] Guid? recipientId = null,
        [FromQuery] DateTime? since = null,
        CancellationToken ct = default)
    {
        return Ok(await chatService.GetMessagesAsync(channel, recipientId, since, ct));
    }

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

