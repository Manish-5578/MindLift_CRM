using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 4 / #26: Visual email template builder (email-template-builder).
/// REST endpoints for block-based visual email template management, preset starter library,
/// and live merge-tag rendering against CRM entities.
/// </summary>
[ApiController]
[Authorize]
[RequiresFeature("email-template-builder")]
[Route("api/v1/email-templates")]
public sealed class EmailTemplatesController(IEmailTemplateService templateService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmailTemplateSummaryDto>>> ListTemplates(
        [FromQuery] string? category,
        [FromQuery] string? search,
        [FromQuery] bool? activeOnly,
        CancellationToken ct)
    {
        var list = await templateService.ListTemplatesAsync(category, search, activeOnly, ct);
        return Ok(list);
    }

    [HttpGet("presets")]
    public async Task<ActionResult<IReadOnlyList<EmailTemplatePresetDto>>> GetPresets(CancellationToken ct)
    {
        var presets = await templateService.GetPresetsAsync(ct);
        return Ok(presets);
    }

    [HttpPost("presets/{key}/create")]
    public async Task<ActionResult<EmailTemplateDto>> CreateFromPreset(
        string key,
        [FromBody] CloneEmailTemplateRequest? request,
        CancellationToken ct)
    {
        try
        {
            var created = await templateService.CreateFromPresetAsync(key, request?.NewName, ct);
            return CreatedAtAction(nameof(GetTemplateById), new { id = created.Id }, created);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Template preset '{key}' not found." });
        }
    }

    [HttpPost("render-preview")]
    public async Task<ActionResult<RenderedEmailResult>> RenderPreview(
        [FromBody] RenderTemplateRequest request,
        CancellationToken ct)
    {
        var result = await templateService.RenderPreviewAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmailTemplateDto>> GetTemplateById(Guid id, CancellationToken ct)
    {
        var template = await templateService.GetTemplateByIdAsync(id, ct);
        if (template == null) return NotFound();
        return Ok(template);
    }

    [HttpPost]
    public async Task<ActionResult<EmailTemplateDto>> CreateTemplate(
        [FromBody] SaveEmailTemplateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Template name is required." });
        }

        var created = await templateService.CreateTemplateAsync(request, ct);
        return CreatedAtAction(nameof(GetTemplateById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmailTemplateDto>> UpdateTemplate(
        Guid id,
        [FromBody] SaveEmailTemplateRequest request,
        CancellationToken ct)
    {
        var updated = await templateService.UpdateTemplateAsync(id, request, ct);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        var deleted = await templateService.DeleteTemplateAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/clone")]
    public async Task<ActionResult<EmailTemplateDto>> CloneTemplate(
        Guid id,
        [FromBody] CloneEmailTemplateRequest request,
        CancellationToken ct)
    {
        var cloned = await templateService.CloneTemplateAsync(id, request, ct);
        if (cloned == null) return NotFound();
        return Ok(cloned);
    }

    [HttpPost("{id:guid}/render")]
    public async Task<ActionResult<RenderedEmailResult>> RenderWithEntity(
        Guid id,
        [FromBody] RenderWithEntityRequest request,
        CancellationToken ct)
    {
        var result = await templateService.RenderTemplateWithEntityAsync(id, request, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }
}
