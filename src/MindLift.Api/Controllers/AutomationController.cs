using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/automation")]
[RequiresModule(AppModule.Automation)]
public sealed class AutomationController(IAutomationEngine automation) : ControllerBase
{
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("rules")]
    public async Task<ActionResult<IReadOnlyList<AutomationRuleDto>>> ListRules(CancellationToken cancellationToken)
        => Ok(await automation.ListRulesAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("rules/{id:guid}")]
    public async Task<ActionResult<AutomationRuleDto>> GetRule(Guid id, CancellationToken cancellationToken)
        => Ok(await automation.GetRuleAsync(id, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("rules")]
    public async Task<ActionResult<AutomationRuleDto>> CreateRule([FromBody] SaveAutomationRuleRequest request, CancellationToken cancellationToken)
        => Ok(await automation.SaveRuleAsync(null, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("rules/{id:guid}")]
    public async Task<ActionResult<AutomationRuleDto>> UpdateRule(Guid id, [FromBody] SaveAutomationRuleRequest request, CancellationToken cancellationToken)
        => Ok(await automation.SaveRuleAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpDelete("rules/{id:guid}")]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken cancellationToken)
    {
        await automation.DeleteRuleAsync(id, cancellationToken);
        return NoContent();
    }

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("rules/{id:guid}/toggle")]
    public async Task<ActionResult<AutomationRuleDto>> ToggleRule(Guid id, CancellationToken cancellationToken)
        => Ok(await automation.ToggleRuleAsync(id, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("logs")]
    public async Task<ActionResult<IReadOnlyList<AutomationExecutionLogDto>>> ListLogs([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
        => Ok(await automation.ListLogsAsync(limit, cancellationToken));
}
