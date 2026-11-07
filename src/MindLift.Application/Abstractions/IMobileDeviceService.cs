using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IMobileDeviceService
{
    Task<MobileDeviceDto> RegisterOrUpdateCurrentAsync(RegisterMobileDeviceRequest request, CancellationToken ct = default);
    Task RevokeCurrentAsync(string installationId, CancellationToken ct = default);
    Task HeartbeatAsync(string installationId, string? appVersion, CancellationToken ct = default);
    Task<IReadOnlyList<MobileDeviceDto>> GetUserDevicesAsync(Guid userId, CancellationToken ct = default);
    Task RecordPushSuccessAsync(Guid deviceId, CancellationToken ct = default);
    Task RecordPushFailureAsync(Guid deviceId, string failureCode, CancellationToken ct = default);
}

