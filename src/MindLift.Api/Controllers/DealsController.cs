using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Notifications;
using MindLift.Infrastructure.Deals;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/deals")]
public sealed class DealsController(DealService deals, ImportNotifier imports) : ControllerBase
{
    [HasPermission(AppModule.Deals, PermissionAction.View)]
    [HttpPost("query")]
    public async Task<ActionResult<PagedResult<DealListItemDto>>> Query([FromBody] ListQuery query, CancellationToken cancellationToken)
        => Ok(await deals.QueryAsync(query, cancellationToken));

    /// <summary>The kanban board of one pipeline. The optional filter narrows which deals appear.</summary>
    [HasPermission(AppModule.Deals, PermissionAction.View)]
    [HttpPost("board")]
    public async Task<ActionResult<DealBoardDto>> Board([FromQuery] Guid? pipelineId, [FromBody] FilterGroup? filter, CancellationToken cancellationToken)
        => Ok(await deals.BoardAsync(pipelineId, filter, cancellationToken));

    [HasPermission(AppModule.Deals, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DealDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await deals.GetAsync(id, cancellationToken));

    [HasPermission(AppModule.Deals, PermissionAction.View)]
    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<DealStageHistoryDto>>> History(Guid id, CancellationToken cancellationToken)
        => Ok(await deals.HistoryAsync(id, cancellationToken));

    [HasPermission(AppModule.Deals, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<DealDto>> Create([FromBody] SaveDealRequest request, CancellationToken cancellationToken)
        => Ok(await deals.CreateAsync(request, cancellationToken));

    [HasPermission(AppModule.Deals, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DealDto>> Update(Guid id, [FromBody] SaveDealRequest request, CancellationToken cancellationToken)
        => Ok(await deals.UpdateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Deals, PermissionAction.Edit)]
    [HttpPatch("{id:guid}/stage")]
    public async Task<ActionResult<DealDto>> ChangeStage(Guid id, [FromBody] ChangeDealStageRequest request, CancellationToken cancellationToken)
        => Ok(await deals.ChangeStageAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Deals, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deals.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Change many deals at once (the list's bulk bar). Each one is checked and saved as if edited on its own page.</summary>
    [HasPermission(AppModule.Deals, PermissionAction.Edit)]
    [HttpPost("bulk")]
    public async Task<ActionResult<BulkActionResult>> Bulk([FromBody] BulkActionRequest request, CancellationToken cancellationToken)
        => Ok(await deals.BulkAsync(request, cancellationToken));

    [HasPermission(AppModule.Deals, PermissionAction.Delete)]
    [HttpPost("bulk-delete")]
    public async Task<ActionResult<BulkActionResult>> BulkDelete([FromBody] BulkDeleteRequest request, CancellationToken cancellationToken)
        => Ok(await deals.BulkDeleteAsync(request, cancellationToken));

    /// <summary>CSV body (text/csv). Max 5,000 rows.</summary>
    [HasPermission(AppModule.Deals, PermissionAction.Import)]
    [HttpPost("import")]
    public async Task<ActionResult<ImportResultDto>> Import(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var result = await deals.ImportCsvAsync(await reader.ReadToEndAsync(cancellationToken), cancellationToken);
        await imports.NotifyAsync("deals", "/deals", result, cancellationToken);
        return Ok(result);
    }

    [HasPermission(AppModule.Deals, PermissionAction.Export)]
    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ListQuery query, CancellationToken cancellationToken)
    {
        var csv = await deals.ExportCsvAsync(query, cancellationToken);
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", "deals.csv");
    }
}
