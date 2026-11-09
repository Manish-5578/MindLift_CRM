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

    /// <summary>The phones and tablets signed in to the caller's account that can receive push.</summary>
    [HttpGet("devices")]
    public async Task<ActionResult<IReadOnlyList<MobileDeviceDto>>> MyDevices(CancellationToken ct)
        => Ok(await deviceService.GetUserDevicesAsync(tenant.UserId!.Value, ct));

    [HttpDelete("devices/{id:guid}")]
    public async Task<IActionResult> RemoveDevice(Guid id, CancellationToken ct)
    {
        await deviceService.RevokeByIdAsync(id, ct);
        return NoContent();
    }

    /// <summary>Pushes to this phone are held back between the two times (device time). Send both as null to turn it off.</summary>
    [HttpPut("devices/current/quiet-hours")]
    public async Task<IActionResult> QuietHours([FromBody] SetQuietHoursRequest request, CancellationToken ct)
    {
        await deviceService.SetQuietHoursAsync(request.InstallationId, request.StartMinute, request.EndMinute, ct);
        return NoContent();
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

