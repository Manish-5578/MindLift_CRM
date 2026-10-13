using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Activities;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/activities")]
public sealed class ActivitiesController(ActivityService activities) : ControllerBase
{
    [HasPermission(AppModule.Activities, PermissionAction.View)]
    [HttpPost("query")]
    public async Task<ActionResult<PagedResult<ActivityDto>>> Query([FromBody] ActivityQuery query, CancellationToken cancellationToken)
        => Ok(await activities.QueryAsync(query, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.View)]
    [HttpGet("my-day")]
    public async Task<ActionResult<MyDayDto>> MyDay(CancellationToken cancellationToken)
        => Ok(await activities.MyDayAsync(cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ActivityDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await activities.GetAsync(id, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<ActivityDto>> Create([FromBody] SaveActivityRequest request, CancellationToken cancellationToken)
        => Ok(await activities.CreateAsync(request, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ActivityDto>> Update(Guid id, [FromBody] SaveActivityRequest request, CancellationToken cancellationToken)
        => Ok(await activities.UpdateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.Edit)]
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<ActivityDto>> Complete(Guid id, [FromBody] CompleteActivityRequest request, CancellationToken cancellationToken)
        => Ok(await activities.CompleteAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.Edit)]
    [HttpPatch("{id:guid}/schedule")]
    public async Task<ActionResult<ActivityDto>> Schedule(Guid id, [FromBody] ScheduleActivityRequest request, CancellationToken cancellationToken)
        => Ok(await activities.ScheduleAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.Edit)]
    [HttpPost("{id:guid}/reopen")]
    public async Task<ActionResult<ActivityDto>> Reopen(Guid id, CancellationToken cancellationToken)
        => Ok(await activities.ReopenAsync(id, cancellationToken));

    [HasPermission(AppModule.Activities, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await activities.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>The checklist tasks of a won deal and how many are done.</summary>
    [HasPermission(AppModule.Deals, PermissionAction.View)]
    [HttpGet("onboarding/{dealId:guid}")]
    public async Task<ActionResult<IReadOnlyList<OnboardingProgressDto>>> Onboarding(Guid dealId, CancellationToken cancellationToken)
        => Ok(await activities.OnboardingAsync(dealId, cancellationToken));
}

/// <summary>Onboarding checklist templates, managed in Settings.</summary>
[ApiController]
[Route("api/v1/settings/checklists")]
public sealed class ChecklistTemplatesController(ChecklistService checklists) : ControllerBase
{
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChecklistTemplateDto>>> List(CancellationToken cancellationToken)
        => Ok(await checklists.ListAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<ChecklistTemplateDto>> Create([FromBody] SaveChecklistTemplateRequest request, CancellationToken cancellationToken)
        => Ok(await checklists.SaveAsync(null, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ChecklistTemplateDto>> Update(Guid id, [FromBody] SaveChecklistTemplateRequest request, CancellationToken cancellationToken)
        => Ok(await checklists.SaveAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await checklists.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>The timeline of a lead, contact, account or deal. The module permission is checked inside, because the module depends on the record.</summary>
[ApiController]
[Route("api/v1/timeline")]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class TimelineController(TimelineService timeline) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TimelineEntryDto>>> Get([FromQuery] string entityType, [FromQuery] Guid entityId, CancellationToken cancellationToken)
        => Ok(await timeline.GetAsync(entityType, entityId, cancellationToken));
}
