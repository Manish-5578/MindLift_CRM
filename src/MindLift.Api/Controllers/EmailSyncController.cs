using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 5 / #29: Two-way email sync (email-sync).
/// REST endpoints for connecting sales rep inboxes, bi-directional email syncing,
/// automatic CRM linking, email sending, and privacy-aware message tracking.
/// </summary>
[ApiController]
[Authorize]
[RequiresFeature("email-sync")]
[Route("api/v1/email-sync")]
public sealed class EmailSyncController(IEmailSyncService emailSyncService) : ControllerBase
{
    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<EmailSyncAccountDto>>> ListAccounts(CancellationToken ct)
    {
        var accounts = await emailSyncService.ListAccountsAsync(ct);
        return Ok(accounts);
    }

    [HttpGet("accounts/{id:guid}")]
    public async Task<ActionResult<EmailSyncAccountDto>> GetAccount(Guid id, CancellationToken ct)
    {
        var account = await emailSyncService.GetAccountAsync(id, ct);
        if (account == null) return NotFound();
        return Ok(account);
    }

    [HttpPost("accounts")]
    public async Task<ActionResult<EmailSyncAccountDto>> CreateAccount(
        [FromBody] CreateEmailSyncAccountRequest request,
        CancellationToken ct)
    {
        var created = await emailSyncService.CreateAccountAsync(request, ct);
        return CreatedAtAction(nameof(GetAccount), new { id = created.Id }, created);
    }

    [HttpPut("accounts/{id:guid}")]
    public async Task<ActionResult<EmailSyncAccountDto>> UpdateAccount(
        Guid id,
        [FromBody] UpdateEmailSyncAccountRequest request,
        CancellationToken ct)
    {
        var updated = await emailSyncService.UpdateAccountAsync(id, request, ct);
        return Ok(updated);
    }

    [HttpDelete("accounts/{id:guid}")]
    public async Task<IActionResult> DeleteAccount(Guid id, CancellationToken ct)
    {
        var deleted = await emailSyncService.DeleteAccountAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("test-connection")]
    public async Task<ActionResult<TestMailboxConnectionResult>> TestConnection(
        [FromBody] TestMailboxConnectionRequest request,
        CancellationToken ct)
    {
        var result = await emailSyncService.TestConnectionAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("accounts/{id:guid}/sync")]
    public async Task<ActionResult<EmailSyncResultDto>> SyncAccount(Guid id, CancellationToken ct)
    {
        var result = await emailSyncService.SyncAccountAsync(id, ct);
        return Ok(result);
    }

    [HttpGet("messages")]
    public async Task<ActionResult<IReadOnlyList<EmailSyncMessageDto>>> ListMessages(
        [FromQuery] Guid? accountId,
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var messages = await emailSyncService.ListMessagesAsync(accountId, entityType, entityId, page, pageSize, ct);
        return Ok(messages);
    }

    [HttpGet("messages/{id:guid}")]
    public async Task<ActionResult<EmailSyncMessageDto>> GetMessage(Guid id, CancellationToken ct)
    {
        var message = await emailSyncService.GetMessageAsync(id, ct);
        if (message == null) return NotFound();
        return Ok(message);
    }

    [HttpPost("accounts/{id:guid}/send")]
    public async Task<ActionResult<EmailSyncMessageDto>> SendEmail(
        Guid id,
        [FromBody] SendSyncEmailRequest request,
        CancellationToken ct)
    {
        var sent = await emailSyncService.SendEmailAsync(id, request, ct);
        return Ok(sent);
    }

    [HttpPost("accounts/{id:guid}/simulate-inbound")]
    public async Task<ActionResult<EmailSyncMessageDto>> SimulateInbound(
        Guid id,
        [FromBody] SimulateInboundEmailRequest request,
        CancellationToken ct)
    {
        var message = await emailSyncService.IngestInboundAsync(id, request, ct);
        return Ok(message);
    }

    [HttpGet("logs")]
    public async Task<ActionResult<IReadOnlyList<EmailSyncLogDto>>> ListLogs(
        [FromQuery] Guid? accountId,
        [FromQuery] int count = 50,
        CancellationToken ct = default)
    {
        var logs = await emailSyncService.ListLogsAsync(accountId, count, ct);
        return Ok(logs);
    }

    [HttpGet("stats")]
    public async Task<ActionResult<EmailSyncStatsDto>> GetStats(CancellationToken ct)
    {
        var stats = await emailSyncService.GetStatsAsync(ct);
        return Ok(stats);
    }
}
