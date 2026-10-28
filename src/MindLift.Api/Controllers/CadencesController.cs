using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 4 / #21: Sales Cadences (cadences).
/// Endpoints for managing multi-channel prospecting sequences, prospect enrollment,
/// step execution, and reply detection.
/// </summary>
[ApiController]
[Authorize]
[RequiresFeature("cadences")]
[Route("api/v1/cadences")]
public sealed class CadencesController(ICadenceService cadenceService) : ControllerBase
{
    // ================= Cadences =================

    [HttpPost]
    public async Task<ActionResult<CadenceDto>> Create(
        [FromBody] SaveCadenceRequest request,
        CancellationToken ct)
    {
        var created = await cadenceService.CreateCadenceAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CadenceDto>>> List(
        [FromQuery] string? targetType,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var list = await cadenceService.ListCadencesAsync(targetType, status, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CadenceDto>> GetById(
        Guid id,
        CancellationToken ct)
    {
        var cadence = await cadenceService.GetCadenceByIdAsync(id, ct);
        return Ok(cadence);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CadenceDto>> Update(
        Guid id,
        [FromBody] SaveCadenceRequest request,
        CancellationToken ct)
    {
        var updated = await cadenceService.UpdateCadenceAsync(id, request, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken ct)
    {
        var deleted = await cadenceService.DeleteCadenceAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<CadenceDto>> SetStatus(
        Guid id,
        [FromQuery] string status,
        CancellationToken ct)
    {
        var updated = await cadenceService.SetCadenceStatusAsync(id, status, ct);
        return Ok(updated);
    }

    // ================= Enrollments =================

    [HttpPost("enrollments")]
    public async Task<ActionResult<CadenceEnrollmentDto>> EnrollTarget(
        [FromBody] EnrollCadenceTargetRequest request,
        CancellationToken ct)
    {
        var enrollment = await cadenceService.EnrollTargetAsync(request, ct);
        return Ok(enrollment);
    }

    [HttpGet("enrollments")]
    public async Task<ActionResult<IReadOnlyList<CadenceEnrollmentDto>>> ListEnrollments(
        [FromQuery] Guid? cadenceId,
        [FromQuery] string? targetType,
        [FromQuery] Guid? targetId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var list = await cadenceService.ListEnrollmentsAsync(cadenceId, targetType, targetId, status, ct);
        return Ok(list);
    }

    [HttpGet("enrollments/due")]
    public async Task<ActionResult<IReadOnlyList<CadenceEnrollmentDto>>> GetDue(
        CancellationToken ct)
    {
        var list = await cadenceService.GetDueEnrollmentsAsync(ct);
        return Ok(list);
    }

    [HttpGet("enrollments/{id:guid}")]
    public async Task<ActionResult<CadenceEnrollmentDto>> GetEnrollmentById(
        Guid id,
        CancellationToken ct)
    {
        var enrollment = await cadenceService.GetEnrollmentByIdAsync(id, ct);
        return Ok(enrollment);
    }

    [HttpPost("enrollments/{id:guid}/pause")]
    public async Task<ActionResult<CadenceEnrollmentDto>> Pause(
        Guid id,
        CancellationToken ct)
    {
        var updated = await cadenceService.PauseEnrollmentAsync(id, ct);
        return Ok(updated);
    }

    [HttpPost("enrollments/{id:guid}/resume")]
    public async Task<ActionResult<CadenceEnrollmentDto>> Resume(
        Guid id,
        CancellationToken ct)
    {
        var updated = await cadenceService.ResumeEnrollmentAsync(id, ct);
        return Ok(updated);
    }

    [HttpPost("enrollments/{id:guid}/unenroll")]
    public async Task<ActionResult<CadenceEnrollmentDto>> Unenroll(
        Guid id,
        [FromQuery] string? reason,
        CancellationToken ct)
    {
        var updated = await cadenceService.UnenrollAsync(id, reason, ct);
        return Ok(updated);
    }

    [HttpPost("enrollments/{id:guid}/replied")]
    public async Task<ActionResult<CadenceEnrollmentDto>> MarkReplied(
        Guid id,
        [FromQuery] string? notes,
        CancellationToken ct)
    {
        var updated = await cadenceService.MarkRepliedAsync(id, notes, ct);
        return Ok(updated);
    }

    [HttpPost("enrollments/{id:guid}/execute")]
    public async Task<ActionResult<CadenceEnrollmentDto>> ExecuteStep(
        Guid id,
        [FromBody] ExecuteCadenceStepRequest request,
        CancellationToken ct)
    {
        var updated = await cadenceService.ExecuteStepAsync(id, request, ct);
        return Ok(updated);
    }
}
