using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 2 / #9: Dependent Picklists (dependent-picklists).
/// Provides CRUD management, allowed value evaluation, and payload validation for parent/child picklist dependencies.
/// </summary>
[ApiController]
[Route("api/v1/dependent-picklists")]
[RequiresFeature("dependent-picklists")]
[Authorize]
public sealed class DependentPicklistsController(IDependentPicklistService dependentPicklists) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DependentPicklistMapDto>>> List(
        [FromQuery] string? entityType,
        CancellationToken cancellationToken)
        => Ok(await dependentPicklists.ListAsync(entityType, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DependentPicklistMapDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await dependentPicklists.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<DependentPicklistMapDto>> Create(
        [FromBody] SaveDependentPicklistMapRequest request,
        CancellationToken cancellationToken)
    {
        var created = await dependentPicklists.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DependentPicklistMapDto>> Update(
        Guid id,
        [FromBody] SaveDependentPicklistMapRequest request,
        CancellationToken cancellationToken)
        => Ok(await dependentPicklists.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await dependentPicklists.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("allowed")]
    public async Task<ActionResult<AllowedPicklistValuesDto>> GetAllowed(
        [FromQuery] string entityType,
        [FromQuery] string parentField,
        [FromQuery] string childField,
        [FromQuery] string? parentValue,
        CancellationToken cancellationToken)
        => Ok(await dependentPicklists.GetAllowedValuesAsync(entityType, parentField, childField, parentValue, cancellationToken));

    [HttpPost("validate")]
    public async Task<ActionResult<DependentPicklistValidationResult>> Validate(
        [FromBody] ValidateDependentPicklistRequest request,
        CancellationToken cancellationToken)
        => Ok(await dependentPicklists.ValidateAsync(request.EntityType, request.FieldValues, cancellationToken));
}
