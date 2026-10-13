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

/// <summary>Saved list views (plan 4.6.6). Any user with access to the Dashboard can read and manage their own views.</summary>
[ApiController]
[Route("api/v1/views")]
public sealed class ViewsController(SavedViewService views) : ControllerBase
{
    [HasPermission(AppModule.Dashboard, PermissionAction.View)]
    [HttpGet("{entity}")]
    public async Task<ActionResult<IReadOnlyList<SavedViewDto>>> List(string entity, CancellationToken cancellationToken)
        => Ok(await views.ListAsync(entity, cancellationToken));

    [HasPermission(AppModule.Dashboard, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<SavedViewDto>> Create([FromBody] SaveSavedViewRequest request, CancellationToken cancellationToken)
        => Ok(await views.CreateAsync(request, cancellationToken));

    [HasPermission(AppModule.Dashboard, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await views.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
