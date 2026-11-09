using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Registered mobile device installation for push notifications and mobile lifecycle management.
/// Push tokens are encrypted at rest; TokenHash is stored for lookup and deduplication.
/// </summary>
public sealed class MobileDevice : TenantEntity
{
    public Guid UserId { get; set; }

    /// <summary>App-generated stable UUID per installation.</summary>
    public string InstallationId { get; set; } = string.Empty;

    /// <summary>"android" or "ios"</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>"sandbox" or "production"</summary>
    public string Environment { get; set; } = "production";

    /// <summary>Push provider: "fcm" or "apns"</summary>
    public string Provider { get; set; } = "fcm";
    public string ProviderVersion { get; set; } = "v1";

    /// <summary>Push token encrypted at rest via IDataProtectionProvider.</summary>
    public string TokenCiphertext { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of device token for lookup & deduplication only.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public string AppVersion { get; set; } = string.Empty;
    public string Locale { get; set; } = "en";
    public string TimeZoneId { get; set; } = "UTC";
    public bool NotificationsAllowed { get; set; } = true;

    public DateTime LastSeenAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public DateTime? LastPushSucceededAtUtc { get; set; }
    public string? LastPushFailureCode { get; set; }
    public int FailureCount { get; set; }

    /// <summary>Quiet hours for pushes to this phone, in minutes after local midnight (device time zone). Both null means off.</summary>
    public int? QuietStartMinute { get; set; }
    public int? QuietEndMinute { get; set; }
}

