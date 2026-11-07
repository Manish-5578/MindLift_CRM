using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/mobile")]
public sealed class MobileController(IMobileDeviceService deviceService) : ControllerBase
{
    [HttpPut("devices/current")]
    public async Task<ActionResult<MobileDeviceDto>> RegisterCurrentDevice(
        [FromBody] RegisterMobileDeviceRequest request,
        CancellationToken ct)
    {
        var device = await deviceService.RegisterOrUpdateCurrentAsync(request, ct);
        return Ok(device);
    }

    [HttpDelete("devices/current")]
    public async Task<IActionResult> RevokeCurrentDevice(
        [FromBody] RevokeMobileDeviceRequest request,
        CancellationToken ct)
    {
        await deviceService.RevokeCurrentAsync(request.InstallationId, ct);
        return NoContent();
    }

    [HttpPost("devices/current/heartbeat")]
    public async Task<IActionResult> Heartbeat(
        [FromBody] MobileDeviceHeartbeatRequest request,
        CancellationToken ct)
    {
        await deviceService.HeartbeatAsync(request.InstallationId, request.AppVersion, ct);
        return NoContent();
    }
}

