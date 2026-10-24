using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Tickets;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/tickets")]
public sealed class TicketsController(TicketService tickets) : ControllerBase
{
    [HasPermission(AppModule.Tickets, PermissionAction.View)]
    [HttpPost("query")]
    public async Task<ActionResult<PagedResult<TicketListItemDto>>> Query([FromBody] TicketQuery query, CancellationToken cancellationToken)
        => Ok(await tickets.QueryAsync(query, cancellationToken));

    [HasPermission(AppModule.Tickets, PermissionAction.View)]
    [HttpGet("summary")]
    public async Task<ActionResult<TicketSummaryDto>> Summary(CancellationToken cancellationToken) => Ok(await tickets.SummaryAsync(cancellationToken));

    [HasPermission(AppModule.Tickets, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDto>> Get(Guid id, CancellationToken cancellationToken) => Ok(await tickets.GetAsync(id, cancellationToken));

    [HasPermission(AppModule.Tickets, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<TicketDto>> Create([FromBody] SaveTicketRequest request, CancellationToken cancellationToken)
        => Ok(await tickets.CreateAsync(request, "manual", cancellationToken));

    [HasPermission(AppModule.Tickets, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TicketDto>> Update(Guid id, [FromBody] SaveTicketRequest request, CancellationToken cancellationToken)
        => Ok(await tickets.UpdateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Tickets, PermissionAction.Edit)]
    [HttpPost("{id:guid}/assign")]
    public async Task<ActionResult<TicketDto>> Assign(Guid id, [FromBody] AssignTicketRequest request, CancellationToken cancellationToken)
        => Ok(await tickets.AssignAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Tickets, PermissionAction.Edit)]
    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<TicketDto>> ChangeStatus(Guid id, [FromBody] ChangeTicketStatusRequest request, CancellationToken cancellationToken)
        => Ok(await tickets.ChangeStatusAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Tickets, PermissionAction.Edit)]
    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<TicketDto>> Comment(Guid id, [FromBody] AddTicketCommentRequest request, CancellationToken cancellationToken)
        => Ok(await tickets.AddCommentAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Tickets, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await tickets.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
