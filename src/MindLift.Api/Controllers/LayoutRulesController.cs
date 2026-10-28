using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 2 / #10: Conditional Field Visibility / Layout Rules (layout-rules).
/// Controller for managing, evaluating, and validating dynamic layout rules.
/// </summary>
[ApiController]
[Route("api/v1/layout-rules")]
[RequiresFeature("layout-rules")]
[Authorize]
public sealed class LayoutRulesController(ILayoutRuleService layoutRules) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LayoutRuleDto>>> List(
        [FromQuery] string? entityType,
        CancellationToken cancellationToken)
        => Ok(await layoutRules.ListAsync(entityType, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LayoutRuleDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await layoutRules.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<LayoutRuleDto>> Create(
        [FromBody] SaveLayoutRuleRequest request,
        CancellationToken cancellationToken)
    {
        var created = await layoutRules.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LayoutRuleDto>> Update(
        Guid id,
        [FromBody] SaveLayoutRuleRequest request,
        CancellationToken cancellationToken)
        => Ok(await layoutRules.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await layoutRules.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("evaluate")]
    public async Task<ActionResult<LayoutRuleEvaluationResult>> Evaluate(
        [FromBody] EvaluateLayoutRulesRequest request,
        CancellationToken cancellationToken)
        => Ok(await layoutRules.EvaluateAsync(request.EntityType, request.FieldValues, cancellationToken));

    [HttpPost("validate")]
    public async Task<ActionResult<LayoutRuleValidationResult>> Validate(
        [FromBody] ValidateLayoutRulesRequest request,
        CancellationToken cancellationToken)
        => Ok(await layoutRules.ValidateAsync(request.EntityType, request.FieldValues, cancellationToken));
}
