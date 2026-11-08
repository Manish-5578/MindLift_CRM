using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindLift.Api.Auth;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Leads;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/leads")]
[Authorize]
[HasPermission(AppModule.Leads, PermissionAction.Create)]
[EnableRateLimiting("uploads")]
public sealed class LeadOcrController(LeadCardOcrService ocrService) : ControllerBase
{
    [HttpPost("parse-card")]
    public async Task<ActionResult<ExtractedLeadCardDto>> ParseCard(
        [FromBody] ParseCardOcrRequest request,
        CancellationToken ct = default)
    {
        var result = await ocrService.ExtractAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("scan-card-file")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<ActionResult<ExtractedLeadCardDto>> ScanCardFile(
        IFormFile file,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
        {
            throw new ValidationException("empty_file", "No file was provided for scanning.");
        }

        if (file.Length > 15 * 1024 * 1024)
        {
            throw new ValidationException("file_too_large", "The card image exceeds the 15 MB limit.");
        }

        await using var stream = file.OpenReadStream();
        var result = await ocrService.ExtractFromDirectStreamAsync(stream, file.FileName, ct);
        return Ok(result);
    }
}
