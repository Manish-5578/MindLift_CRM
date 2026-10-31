using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Settings;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>The one-call payload the staff app loads at login (plan 7.2).</summary>
[ApiController]
[Route("api/v1/app")]
public sealed class AppController(BootstrapService bootstrap) : ControllerBase
{
    [HasPermission(AppModule.Dashboard, PermissionAction.View)]
    [HttpGet("bootstrap")]
    public async Task<ActionResult<BootstrapDto>> Bootstrap(CancellationToken cancellationToken)
        => Ok(await bootstrap.GetAsync(cancellationToken));
}

/// <summary>Saved list views (plan 4.6.6). Authorization is checked per entity module in the service.</summary>
[ApiController]
[Authorize]
[Route("api/v1/views")]
public sealed class ViewsController(SavedViewService views) : ControllerBase
{
    [HttpGet("{entity}")]
    public async Task<ActionResult<IReadOnlyList<SavedViewDto>>> List(string entity, CancellationToken cancellationToken)
        => Ok(await views.ListAsync(entity, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<SavedViewDto>> Create([FromBody] SaveSavedViewRequest request, CancellationToken cancellationToken)
        => Ok(await views.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SavedViewDto>> Update(Guid id, [FromBody] SaveSavedViewRequest request, CancellationToken cancellationToken)
        => Ok(await views.UpdateAsync(id, request, cancellationToken));

    [HttpPost("move-record")]
    public async Task<IActionResult> MoveRecord([FromBody] MoveRecordToViewRequest request, CancellationToken cancellationToken)
    {
        await views.MoveRecordAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await views.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
