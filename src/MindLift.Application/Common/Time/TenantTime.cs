using MindLift.Application.Abstractions;

namespace MindLift.Application.Common.Time;

/// <summary>
/// Converts between UTC (storage) and the tenant's local time (display and date math).
/// Stored values are always UTC (plan section 0, rule 8).
/// </summary>
public sealed class TenantTime(TimeZoneInfo zone, IClock clock)
{
    public TimeZoneInfo Zone { get; } = zone;

    public DateTime NowUtc => clock.UtcNow;

    public DateTime ToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime ToUtc(DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    /// <summary>Resolves an IANA id such as "Asia/Dubai". Throws TimeZoneNotFoundException for unknown ids.</summary>
    public static TimeZoneInfo FindZone(string ianaId) => TimeZoneInfo.FindSystemTimeZoneById(ianaId);
}
