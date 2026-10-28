using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 6 / #41: Velocity and funnel reports (velocity-reports).
/// Stage conversion funnel, drop-off rates, and sales velocity equation metrics.
/// </summary>
[ApiController]
[Route("api/v1/reports/velocity")]
[Authorize]
[RequiresFeature("velocity-reports")]
public sealed class VelocityReportsController(IVelocityReportService velocityService) : ControllerBase
{
    [HttpGet("funnel")]
    public async Task<ActionResult<PipelineFunnelReportDto>> GetFunnel(
        [FromQuery] PipelineFunnelReportRequest request,
        CancellationToken ct)
    {
        var report = await velocityService.GetFunnelReportAsync(request, ct);
        return Ok(report);
    }

    [HttpGet("sales-velocity")]
    public async Task<ActionResult<SalesVelocityReportDto>> GetSalesVelocity(
        [FromQuery] SalesVelocityReportRequest request,
        CancellationToken ct)
    {
        var report = await velocityService.GetSalesVelocityAsync(request, ct);
        return Ok(report);
    }
}
