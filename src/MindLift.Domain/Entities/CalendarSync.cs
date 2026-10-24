using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// A person's link to their Google or Microsoft calendar. Their meetings here are copied there, and what is already in that calendar
/// blocks the same times for booking. The tokens are stored encrypted.
/// </summary>
public sealed class CalendarConnection : TenantEntity
{
    public Guid UserId { get; set; }

    /// <summary>"google" or "microsoft".</summary>
    public string Provider { get; set; } = string.Empty;

    public string AccountEmail { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessExpiresAt { get; set; }

    public DateTime? LastSyncAt { get; set; }

    /// <summary>What went wrong the last time, for the settings page. Empty when the last sync worked.</summary>
    public string? LastError { get; set; }
}

/// <summary>Which meeting became which event in the other calendar, so a change updates it instead of adding another.</summary>
public sealed class CalendarEventLink : TenantEntity
{
    public Guid ConnectionId { get; set; }
    public Guid ActivityId { get; set; }
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Fingerprint of what was last sent, so an unchanged meeting is not sent again.</summary>
    public string ContentHash { get; set; } = string.Empty;
}

/// <summary>A time that is taken in a person's other calendar. Only the time is kept, never what the event is.</summary>
public sealed class ExternalBusyBlock : TenantEntity
{
    public Guid ConnectionId { get; set; }
    public Guid UserId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
}

/// <summary>
/// The secret address of a person's calendar feed (.ics), which Google, Outlook and Apple can subscribe to. Only a hash is kept,
/// so the address is shown once when it is made; making a new one stops the old one working.
/// </summary>
public sealed class CalendarFeed : TenantEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
}
