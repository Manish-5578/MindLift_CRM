using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 5 / #35: Shared team inboxes (shared-inbox).
/// REST endpoints for collaborative shared mailboxes, team members, thread assignment,
/// collision detection locks, and internal agent notes.
/// </summary>
[ApiController]
[Authorize]
[RequiresFeature("shared-inbox")]
[Route("api/v1/shared-inbox")]
public sealed class SharedInboxController(
    ISharedInboxService inboxService,
    ITenantContext tenantContext) : ControllerBase
{
    private (Guid UserId, string UserName, string UserEmail) CurrentUser()
    {
        var id = tenantContext.UserId ?? Guid.Empty;
        if (id == Guid.Empty)
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (Guid.TryParse(sub, out var parsed)) id = parsed;
        }

        if (id == Guid.Empty)
            throw new UnauthorizedException("not_signed_in", "User is not signed in.");

        var name = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("name") ?? "Agent";
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "agent@mindlift.crm";

        return (id, name, email);
    }

    // ==========================================
    // Mailbox Endpoints
    // ==========================================

    [HttpPost("mailboxes")]
    public async Task<ActionResult<SharedMailboxDto>> CreateMailbox(
        [FromBody] CreateSharedMailboxRequest request,
        CancellationToken ct)
    {
        var mailbox = await inboxService.CreateMailboxAsync(request, ct);
        return CreatedAtAction(nameof(GetMailbox), new { id = mailbox.Id }, mailbox);
    }

    [HttpGet("mailboxes")]
    public async Task<ActionResult<IReadOnlyList<SharedMailboxDto>>> GetMailboxes(CancellationToken ct)
    {
        var mailboxes = await inboxService.GetMailboxesAsync(ct);
        return Ok(mailboxes);
    }

    [HttpGet("mailboxes/{id:guid}")]
    public async Task<ActionResult<SharedMailboxDto>> GetMailbox(Guid id, CancellationToken ct)
    {
        var mailbox = await inboxService.GetMailboxAsync(id, ct);
        return Ok(mailbox);
    }

    [HttpPut("mailboxes/{id:guid}")]
    public async Task<ActionResult<SharedMailboxDto>> UpdateMailbox(
        Guid id,
        [FromBody] UpdateSharedMailboxRequest request,
        CancellationToken ct)
    {
        var updated = await inboxService.UpdateMailboxAsync(id, request, ct);
        return Ok(updated);
    }

    // ==========================================
    // Mailbox Members
    // ==========================================

    [HttpPost("mailboxes/{id:guid}/members")]
    public async Task<ActionResult<SharedMailboxMemberDto>> AddMember(
        Guid id,
        [FromBody] AddMailboxMemberRequest request,
        CancellationToken ct)
    {
        var member = await inboxService.AddMemberAsync(id, request, ct);
        return Ok(member);
    }

    [HttpGet("mailboxes/{id:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<SharedMailboxMemberDto>>> GetMembers(
        Guid id,
        CancellationToken ct)
    {
        var members = await inboxService.GetMembersAsync(id, ct);
        return Ok(members);
    }

    [HttpDelete("mailboxes/{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid id,
        Guid userId,
        CancellationToken ct)
    {
        await inboxService.RemoveMemberAsync(id, userId, ct);
        return NoContent();
    }

    // ==========================================
    // Threads
    // ==========================================

    [HttpPost("threads")]
    public async Task<ActionResult<SharedThreadDto>> CreateThread(
        [FromBody] CreateSharedThreadRequest request,
        CancellationToken ct)
    {
        var thread = await inboxService.CreateThreadAsync(request, ct);
        return CreatedAtAction(nameof(GetThread), new { id = thread.Id }, thread);
    }

    [HttpGet("threads")]
    public async Task<ActionResult<IReadOnlyList<SharedThreadDto>>> GetThreads(
        [FromQuery] Guid? mailboxId = null,
        [FromQuery] Guid? assignedUserId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? priority = null,
        [FromQuery] bool? unassignedOnly = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var query = new SharedThreadQuery(mailboxId, assignedUserId, status, priority, unassignedOnly, limit);
        var threads = await inboxService.GetThreadsAsync(query, ct);
        return Ok(threads);
    }

    [HttpGet("threads/{id:guid}")]
    public async Task<ActionResult<SharedThreadDto>> GetThread(Guid id, CancellationToken ct)
    {
        var thread = await inboxService.GetThreadAsync(id, ct);
        return Ok(thread);
    }

    [HttpPut("threads/{id:guid}/assign")]
    public async Task<ActionResult<SharedThreadDto>> AssignThread(
        Guid id,
        [FromBody] AssignThreadRequest request,
        CancellationToken ct)
    {
        var thread = await inboxService.AssignThreadAsync(id, request, ct);
        return Ok(thread);
    }

    [HttpPut("threads/{id:guid}/status")]
    public async Task<ActionResult<SharedThreadDto>> UpdateThreadStatus(
        Guid id,
        [FromBody] UpdateThreadStatusRequest request,
        CancellationToken ct)
    {
        var thread = await inboxService.UpdateThreadStatusAsync(id, request, ct);
        return Ok(thread);
    }

    // ==========================================
    // Messages & Internal Notes
    // ==========================================

    [HttpPost("threads/{id:guid}/messages")]
    public async Task<ActionResult<SharedThreadMessageDto>> PostMessage(
        Guid id,
        [FromBody] PostThreadMessageRequest request,
        CancellationToken ct)
    {
        var (userId, userName, userEmail) = CurrentUser();
        var message = await inboxService.PostMessageAsync(id, request, userId, userName, userEmail, ct);
        return Ok(message);
    }

    [HttpGet("threads/{id:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<SharedThreadMessageDto>>> GetThreadMessages(
        Guid id,
        CancellationToken ct)
    {
        var messages = await inboxService.GetThreadMessagesAsync(id, ct);
        return Ok(messages);
    }

    // ==========================================
    // Collision Detection Locks
    // ==========================================

    [HttpPost("threads/{id:guid}/lock")]
    public async Task<ActionResult<CollisionStatusDto>> AcquireLock(
        Guid id,
        [FromBody] AcquireLockRequest request,
        CancellationToken ct)
    {
        var (userId, userName, _) = CurrentUser();
        var result = await inboxService.AcquireLockAsync(id, request, userId, userName, ct);
        return Ok(result);
    }

    [HttpDelete("threads/{id:guid}/lock")]
    public async Task<IActionResult> ReleaseLock(
        Guid id,
        CancellationToken ct)
    {
        var (userId, _, _) = CurrentUser();
        await inboxService.ReleaseLockAsync(id, userId, ct);
        return NoContent();
    }

    [HttpGet("threads/{id:guid}/lock")]
    public async Task<ActionResult<CollisionStatusDto>> GetCollisionStatus(
        Guid id,
        CancellationToken ct)
    {
        var (userId, _, _) = CurrentUser();
        var status = await inboxService.GetCollisionStatusAsync(id, userId, ct);
        return Ok(status);
    }
}
