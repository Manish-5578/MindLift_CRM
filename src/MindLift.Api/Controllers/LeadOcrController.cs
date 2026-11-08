using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Infrastructure.Leads;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/leads")]
[Authorize]
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
}

