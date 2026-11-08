using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/mobile")]
public sealed class MobileController(IMobileDeviceService deviceService, MindLift.Infrastructure.Mobile.FcmPushNotificationSender push, ITenantContext tenant) : ControllerBase
{
    /// <summary>Sends a test push to the signed-in user's devices and says what happened to each one.</summary>
    [HttpPost("devices/current/test-push")]
    public async Task<IActionResult> TestPush(CancellationToken ct)
    {
        var report = await push.SendAsync(tenant.TenantId!.Value, tenant.UserId!.Value, "MindLift test", "If you can read this, push works.",
            new Dictionary<string, string> { ["type"] = "test", ["route"] = "/notifications" }, ct);
        return Ok(new { results = report });
    }

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

