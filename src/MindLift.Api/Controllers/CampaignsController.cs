using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Campaigns;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/campaigns")]
public sealed class CampaignsController(CampaignService campaigns) : ControllerBase
{
    [HasPermission(AppModule.Campaigns, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CampaignListItemDto>>> List(CancellationToken cancellationToken) => Ok(await campaigns.ListAsync(cancellationToken));

    [HasPermission(AppModule.Campaigns, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CampaignDto>> Get(Guid id, CancellationToken cancellationToken) => Ok(await campaigns.GetAsync(id, cancellationToken));

    [HasPermission(AppModule.Campaigns, PermissionAction.View)]
    [HttpGet("{id:guid}/recipients")]
    public async Task<ActionResult<PagedResult<CampaignRecipientDto>>> Recipients(Guid id, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
        => Ok(await campaigns.RecipientsAsync(id, status, page, pageSize, cancellationToken));

    [HasPermission(AppModule.Campaigns, PermissionAction.View)]
    [HttpPost("audience-preview")]
    public async Task<ActionResult<AudiencePreviewDto>> Preview([FromBody] CampaignAudience audience, CancellationToken cancellationToken) => Ok(await campaigns.PreviewAsync(audience, cancellationToken));

    [HasPermission(AppModule.Campaigns, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<CampaignDto>> Create([FromBody] SaveCampaignRequest request, CancellationToken cancellationToken) => Ok(await campaigns.CreateAsync(request, cancellationToken));

    [HasPermission(AppModule.Campaigns, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CampaignDto>> Update(Guid id, [FromBody] SaveCampaignRequest request, CancellationToken cancellationToken) => Ok(await campaigns.UpdateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Campaigns, PermissionAction.Create)]
    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<CampaignDto>> Duplicate(Guid id, CancellationToken cancellationToken) => Ok(await campaigns.DuplicateAsync(id, cancellationToken));

    [HasPermission(AppModule.Campaigns, PermissionAction.Edit)]
    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> SendTest(Guid id, [FromBody] SendTestRequest request, CancellationToken cancellationToken)
    {
        await campaigns.SendTestAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HasPermission(AppModule.Campaigns, PermissionAction.Edit)]
    [HttpPost("{id:guid}/send")]
    public async Task<ActionResult<CampaignDto>> Send(Guid id, CancellationToken cancellationToken) => Ok(await campaigns.StartAsync(id, cancellationToken));

    [HasPermission(AppModule.Campaigns, PermissionAction.Edit)]
    [HttpPost("{id:guid}/schedule")]
    public async Task<ActionResult<CampaignDto>> Schedule(Guid id, [FromBody] ScheduleCampaignRequest request, CancellationToken cancellationToken) => Ok(await campaigns.ScheduleAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Campaigns, PermissionAction.Edit)]
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<CampaignDto>> Cancel(Guid id, CancellationToken cancellationToken) => Ok(await campaigns.CancelAsync(id, cancellationToken));

    [HasPermission(AppModule.Campaigns, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await campaigns.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>
/// What a campaign's email calls when it is opened or a link in it is clicked. The token in the address names one recipient's copy.
/// A click only goes to a link the campaign's message actually contains, so this can never be used to send people somewhere else.
/// </summary>
[ApiController]
[AllowAnonymous]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("public")]
public sealed class CampaignTrackingController(AppDbContext db, MindLift.Application.Abstractions.IClock clock) : ControllerBase
{
    private static readonly byte[] Pixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    [HttpGet("/t/o/{token}.gif")]
    public async Task<IActionResult> Open(string token, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await db.CampaignRecipients.IgnoreQueryFilters().Where(r => r.Token == token && !r.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.OpenedAt, r => r.OpenedAt ?? now).SetProperty(r => r.OpenCount, r => r.OpenCount + 1), cancellationToken);
        Response.Headers["Cache-Control"] = "no-store";
        return File(Pixel, "image/gif");
    }

    [HttpGet("/t/c/{token}")]
    public async Task<IActionResult> Click(string token, [FromQuery] string? u, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(u, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https"))
        {
            return NotFound();
        }

        var row = await db.CampaignRecipients.IgnoreQueryFilters().AsNoTracking().Where(r => r.Token == token && !r.IsDeleted)
            .Join(db.Campaigns.IgnoreQueryFilters(), r => r.CampaignId, c => c.Id, (r, c) => new { c.Body }).FirstOrDefaultAsync(cancellationToken);
        if (row is null || !CampaignService.LinksIn(row.Body).Contains(u!, StringComparer.Ordinal))
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        await db.CampaignRecipients.IgnoreQueryFilters().Where(r => r.Token == token)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ClickedAt, r => r.ClickedAt ?? now).SetProperty(r => r.ClickCount, r => r.ClickCount + 1)
                .SetProperty(r => r.OpenedAt, r => r.OpenedAt ?? now), cancellationToken);
        return Redirect(target.AbsoluteUri);
    }
}
