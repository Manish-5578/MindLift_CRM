using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 2 / #11: Custom Object Builder (custom-objects).
/// Endpoints for creating and managing custom entities and their dynamic record payloads.
/// </summary>
[ApiController]
[Route("api/v1/custom-objects")]
[RequiresFeature("custom-objects")]
[Authorize]
public sealed class CustomObjectsController(ICustomObjectService customObjects) : ControllerBase
{
    [HttpGet("definitions")]
    public async Task<ActionResult<IReadOnlyList<CustomObjectDefinitionDto>>> ListDefinitions(
        [FromQuery] bool activeOnly,
        CancellationToken cancellationToken)
        => Ok(await customObjects.ListDefinitionsAsync(activeOnly, cancellationToken));

    [HttpGet("definitions/{id:guid}")]
    public async Task<ActionResult<CustomObjectDefinitionDto>> GetDefinition(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await customObjects.GetDefinitionAsync(id, cancellationToken));

    [HttpGet("definitions/by-slug/{slug}")]
    public async Task<ActionResult<CustomObjectDefinitionDto>> GetDefinitionBySlug(
        string slug,
        CancellationToken cancellationToken)
        => Ok(await customObjects.GetDefinitionBySlugAsync(slug, cancellationToken));

    [HttpPost("definitions")]
    public async Task<ActionResult<CustomObjectDefinitionDto>> CreateDefinition(
        [FromBody] SaveCustomObjectDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var created = await customObjects.CreateDefinitionAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetDefinition), new { id = created.Id }, created);
    }

    [HttpPut("definitions/{id:guid}")]
    public async Task<ActionResult<CustomObjectDefinitionDto>> UpdateDefinition(
        Guid id,
        [FromBody] SaveCustomObjectDefinitionRequest request,
        CancellationToken cancellationToken)
        => Ok(await customObjects.UpdateDefinitionAsync(id, request, cancellationToken));

    [HttpDelete("definitions/{id:guid}")]
    public async Task<IActionResult> DeleteDefinition(
        Guid id,
        CancellationToken cancellationToken)
    {
        await customObjects.DeleteDefinitionAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{slug}/records")]
    public async Task<ActionResult<CustomObjectRecordPagedResult>> ListRecords(
        string slug,
        [FromQuery] CustomObjectRecordQuery query,
        CancellationToken cancellationToken)
        => Ok(await customObjects.ListRecordsAsync(slug, query, cancellationToken));

    [HttpGet("{slug}/records/{id:guid}")]
    public async Task<ActionResult<CustomObjectRecordDto>> GetRecord(
        string slug,
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await customObjects.GetRecordAsync(slug, id, cancellationToken));

    [HttpPost("{slug}/records")]
    public async Task<ActionResult<CustomObjectRecordDto>> CreateRecord(
        string slug,
        [FromBody] SaveCustomObjectRecordRequest request,
        CancellationToken cancellationToken)
    {
        var created = await customObjects.CreateRecordAsync(slug, request, cancellationToken);
        return CreatedAtAction(nameof(GetRecord), new { slug, id = created.Id }, created);
    }

    [HttpPut("{slug}/records/{id:guid}")]
    public async Task<ActionResult<CustomObjectRecordDto>> UpdateRecord(
        string slug,
        Guid id,
        [FromBody] SaveCustomObjectRecordRequest request,
        CancellationToken cancellationToken)
        => Ok(await customObjects.UpdateRecordAsync(slug, id, request, cancellationToken));

    [HttpDelete("{slug}/records/{id:guid}")]
    public async Task<IActionResult> DeleteRecord(
        string slug,
        Guid id,
        CancellationToken cancellationToken)
    {
        await customObjects.DeleteRecordAsync(slug, id, cancellationToken);
        return NoContent();
    }
}
