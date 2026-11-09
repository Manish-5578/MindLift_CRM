namespace MindLift.Shared.Contracts;

public sealed record RegisterMobileDeviceRequest(
    string InstallationId,
    string Platform,
    string Environment,
    string DeviceToken,
    string Provider = "fcm",
    string? AppVersion = null,
    string? Locale = "en",
    string? TimeZoneId = "UTC",
    bool NotificationsAllowed = true);

public sealed record MobileDeviceHeartbeatRequest(
    string InstallationId,
    string? AppVersion = null);

public sealed record RevokeMobileDeviceRequest(
    string InstallationId);

public sealed record MobileDeviceDto(
    Guid Id,
    Guid UserId,
    string InstallationId,
    string Platform,
    string Environment,
    string Provider,
    string AppVersion,
    string Locale,
    string TimeZoneId,
    bool NotificationsAllowed,
    DateTime LastSeenAtUtc,
    DateTime? RevokedAtUtc,
    DateTime? LastPushSucceededAtUtc,
    int FailureCount,
    int? QuietStartMinute = null,
    int? QuietEndMinute = null);

public sealed record SetQuietHoursRequest(string InstallationId, int? StartMinute, int? EndMinute);

