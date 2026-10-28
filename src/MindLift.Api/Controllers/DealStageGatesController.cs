using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Deals;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>
/// Stage gating rules configured for a pipeline stage.
/// Hidden (404) until the company enables the "deal-stage-gating" feature.
/// </summary>
[ApiController]
[Route("api/v1/settings/pipelines/{pipelineId:guid}/stages/{stageId:guid}/gates")]
[RequiresFeature("deal-stage-gating")]
public sealed class DealStageGatesController(DealStageGateService stageGates) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<DealStageGateDto>>> List(
        Guid pipelineId,
        Guid stageId,
        CancellationToken ct)
        => Ok(await stageGates.GetGatesAsync(pipelineId, stageId, ct));

    [HttpPost]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<DealStageGateDto>> Create(
        Guid pipelineId,
        Guid stageId,
        [FromBody] SaveDealStageGateRequest request,
        CancellationToken ct)
        => Ok(await stageGates.CreateGateAsync(pipelineId, stageId, request, ct));

    [HttpPut("{gateId:guid}")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<DealStageGateDto>> Update(
        Guid pipelineId,
        Guid stageId,
        Guid gateId,
        [FromBody] SaveDealStageGateRequest request,
        CancellationToken ct)
        => Ok(await stageGates.UpdateGateAsync(pipelineId, stageId, gateId, request, ct));

    [HttpDelete("{gateId:guid}")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<IActionResult> Delete(
        Guid pipelineId,
        Guid stageId,
        Guid gateId,
        CancellationToken ct)
    {
        await stageGates.DeleteGateAsync(pipelineId, stageId, gateId, ct);
        return NoContent();
    }
}
