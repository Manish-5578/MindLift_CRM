using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Common;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Mobile;

public sealed class MobileDeviceService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    MindLift.Infrastructure.Communication.ISecretProtector secretProtector,
    ILogger<MobileDeviceService> logger) : IMobileDeviceService
{
    public async Task<MobileDeviceDto> RegisterOrUpdateCurrentAsync(RegisterMobileDeviceRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.InstallationId))
        {
            throw new ValidationException("InstallationId", "InstallationId is required.");
        }
        if (string.IsNullOrWhiteSpace(request.DeviceToken))
        {
            throw new ValidationException("DeviceToken", "DeviceToken is required.");
        }

        var tenantId = tenant.TenantId ?? throw new ForbiddenException("Tenant context required.", "no_tenant");
        var userId = tenant.UserId ?? throw new ForbiddenException("User context required.", "no_user");
        var now = clock.UtcNow;

        var tokenCiphertext = secretProtector.Protect(request.DeviceToken.Trim()) ?? throw new InvalidOperationException("Device token could not be protected.");
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.DeviceToken.Trim()))).ToLowerInvariant();

        var existing = await db.MobileDevices
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.UserId == userId && d.InstallationId == request.InstallationId.Trim(), ct);

        if (existing is not null)
        {
            existing.Platform = request.Platform.Trim().ToLowerInvariant();
            existing.Environment = request.Environment.Trim().ToLowerInvariant();
            existing.Provider = request.Provider.Trim().ToLowerInvariant();
            existing.TokenCiphertext = tokenCiphertext;
            existing.TokenHash = tokenHash;
            existing.AppVersion = request.AppVersion?.Trim() ?? existing.AppVersion;
            existing.Locale = request.Locale?.Trim() ?? existing.Locale;
            existing.TimeZoneId = request.TimeZoneId?.Trim() ?? existing.TimeZoneId;
            existing.NotificationsAllowed = request.NotificationsAllowed;
            existing.LastSeenAtUtc = now;
            existing.RevokedAtUtc = null;
            existing.UpdatedAt = now;
        }
        else
        {
            existing = new MobileDevice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                InstallationId = request.InstallationId.Trim(),
                Platform = request.Platform.Trim().ToLowerInvariant(),
                Environment = request.Environment.Trim().ToLowerInvariant(),
                Provider = request.Provider.Trim().ToLowerInvariant(),
                TokenCiphertext = tokenCiphertext,
                TokenHash = tokenHash,
                AppVersion = request.AppVersion?.Trim() ?? "1.0.0",
                Locale = request.Locale?.Trim() ?? "en",
                TimeZoneId = request.TimeZoneId?.Trim() ?? "UTC",
                NotificationsAllowed = request.NotificationsAllowed,
                LastSeenAtUtc = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.MobileDevices.Add(existing);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Mobile device registered/updated for user {UserId}, installation {InstallationId}", userId, request.InstallationId);

        return ToDto(existing);
    }

    public async Task RevokeCurrentAsync(string installationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installationId)) return;

        var tenantId = tenant.TenantId ?? throw new ForbiddenException("Tenant context required.", "no_tenant");
        var userId = tenant.UserId ?? throw new ForbiddenException("User context required.", "no_user");
        var now = clock.UtcNow;

        var device = await db.MobileDevices
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.UserId == userId && d.InstallationId == installationId.Trim(), ct);

        if (device is not null)
        {
            device.RevokedAtUtc = now;
            device.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Mobile device revoked for user {UserId}, installation {InstallationId}", userId, installationId);
        }
    }

    public async Task HeartbeatAsync(string installationId, string? appVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installationId)) return;

        var tenantId = tenant.TenantId ?? throw new ForbiddenException("Tenant context required.", "no_tenant");
        var userId = tenant.UserId ?? throw new ForbiddenException("User context required.", "no_user");
        var now = clock.UtcNow;

        var device = await db.MobileDevices
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.UserId == userId && d.InstallationId == installationId.Trim(), ct);

        if (device is not null)
        {
            device.LastSeenAtUtc = now;
            if (!string.IsNullOrWhiteSpace(appVersion))
            {
                device.AppVersion = appVersion.Trim();
            }
            device.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<MobileDeviceDto>> GetUserDevicesAsync(Guid userId, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("Tenant context required.", "no_tenant");
        var devices = await db.MobileDevices
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.UserId == userId && d.RevokedAtUtc == null)
            .OrderByDescending(d => d.LastSeenAtUtc)
            .ToListAsync(ct);

        return devices.Select(ToDto).ToList();
    }

    public async Task RecordPushSuccessAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await db.MobileDevices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is not null)
        {
            device.LastPushSucceededAtUtc = clock.UtcNow;
            device.FailureCount = 0;
            device.LastPushFailureCode = null;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RecordPushFailureAsync(Guid deviceId, string failureCode, CancellationToken ct = default)
    {
        var device = await db.MobileDevices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is not null)
        {
            device.FailureCount++;
            device.LastPushFailureCode = failureCode;
            if (failureCode == "UNREGISTERED" || failureCode == "410" || failureCode == "INVALID_ARGUMENT")
            {
                device.RevokedAtUtc = clock.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private static MobileDeviceDto ToDto(MobileDevice d) => new(
        d.Id,
        d.UserId,
        d.InstallationId,
        d.Platform,
        d.Environment,
        d.Provider,
        d.AppVersion,
        d.Locale,
        d.TimeZoneId,
        d.NotificationsAllowed,
        d.LastSeenAtUtc,
        d.RevokedAtUtc,
        d.LastPushSucceededAtUtc,
        d.FailureCount);
}

