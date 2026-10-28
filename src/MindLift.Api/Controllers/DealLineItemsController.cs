using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Deals;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>
/// Product and service line items attached to deals.
/// Hidden (404) until the company enables the "deal-line-items" feature.
/// </summary>
[ApiController]
[Route("api/v1/deals/{dealId:guid}/line-items")]
[RequiresFeature("deal-line-items")]
public sealed class DealLineItemsController(DealLineItemService lineItems) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Deals, PermissionAction.View)]
    public async Task<ActionResult<DealLineItemsSummaryDto>> Get(Guid dealId, CancellationToken ct)
        => Ok(await lineItems.GetSummaryAsync(dealId, ct));

    [HttpPut]
    [HasPermission(AppModule.Deals, PermissionAction.Edit)]
    public async Task<ActionResult<DealLineItemsSummaryDto>> SetAll(
        Guid dealId,
        [FromBody] SetDealLineItemsRequest request,
        CancellationToken ct)
        => Ok(await lineItems.SetLineItemsAsync(dealId, request, ct));

    [HttpPost]
    [HasPermission(AppModule.Deals, PermissionAction.Edit)]
    public async Task<ActionResult<DealLineItemsSummaryDto>> Add(
        Guid dealId,
        [FromBody] SaveDealLineItemRequest request,
        CancellationToken ct)
        => Ok(await lineItems.AddLineItemAsync(dealId, request, ct));

    [HttpDelete("{lineItemId:guid}")]
    [HasPermission(AppModule.Deals, PermissionAction.Edit)]
    public async Task<ActionResult<DealLineItemsSummaryDto>> Delete(
        Guid dealId,
        Guid lineItemId,
        CancellationToken ct)
        => Ok(await lineItems.DeleteLineItemAsync(dealId, lineItemId, ct));
}
