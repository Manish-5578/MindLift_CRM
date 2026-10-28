using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 4 / #28: Automated data enrichment (data-enrichment).
/// REST endpoints for domain/email firmographic lookup, lead/account auto-enrichment,
/// and enrichment audit logs.
/// </summary>
[ApiController]
[Authorize]
[RequiresFeature("data-enrichment")]
[Route("api/v1/enrichment")]
public sealed class EnrichmentController(IDataEnrichmentService enrichmentService) : ControllerBase
{
    [HttpGet("lookup")]
    public async Task<ActionResult<CompanyEnrichmentData>> Lookup(
        [FromQuery] string? domain,
        [FromQuery] string? email,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(domain) && string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "Either domain or email must be provided for lookup." });
        }

        var result = await enrichmentService.LookupAsync(domain, email, ct);
        if (result == null) return NotFound(new { message = "No company intelligence found for the provided identifier." });
        return Ok(result);
    }

    [HttpPost("leads/{id:guid}")]
    public async Task<ActionResult<EnrichmentResultDto>> EnrichLead(
        Guid id,
        [FromQuery] bool? overwrite,
        CancellationToken ct)
    {
        var result = await enrichmentService.EnrichLeadAsync(id, overwrite, ct);
        if (result.Status == "NotFound") return NotFound();
        return Ok(result);
    }

    [HttpPost("accounts/{id:guid}")]
    public async Task<ActionResult<EnrichmentResultDto>> EnrichAccount(
        Guid id,
        [FromQuery] bool? overwrite,
        CancellationToken ct)
    {
        var result = await enrichmentService.EnrichAccountAsync(id, overwrite, ct);
        if (result.Status == "NotFound") return NotFound();
        return Ok(result);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<IReadOnlyList<EnrichmentResultDto>>> BatchEnrich(
        [FromBody] BatchEnrichRequest request,
        CancellationToken ct)
    {
        var results = await enrichmentService.BatchEnrichAsync(request, ct);
        return Ok(results);
    }

    [HttpGet("logs")]
    public async Task<ActionResult<IReadOnlyList<EnrichmentLogDto>>> ListLogs(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        CancellationToken ct)
    {
        var logs = await enrichmentService.ListLogsAsync(entityType, entityId, ct);
        return Ok(logs);
    }

    [HttpGet("config")]
    public async Task<ActionResult<EnrichmentConfigDto>> GetConfig(CancellationToken ct)
    {
        var config = await enrichmentService.GetConfigAsync(ct);
        return Ok(config);
    }

    [HttpPut("config")]
    public async Task<ActionResult<EnrichmentConfigDto>> UpdateConfig(
        [FromBody] SaveEnrichmentConfigRequest request,
        CancellationToken ct)
    {
        var updated = await enrichmentService.UpdateConfigAsync(request, ct);
        return Ok(updated);
    }
}
