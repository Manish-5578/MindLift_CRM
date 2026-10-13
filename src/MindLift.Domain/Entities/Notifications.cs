using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>One message in a person's notification list (plan 5.5).</summary>
public sealed class Notification : TenantEntity
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    /// <summary>Where clicking the notification goes, for example /leads/{id}.</summary>
    public string? Url { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    /// <summary>Notifications with the same key within ten minutes share one email.</summary>
    public string? GroupKey { get; set; }

    public DateTime? EmailedAt { get; set; }
}

/// <summary>Whether a person wants a type of notification in the app and by email. Missing rows use the defaults.</summary>
public sealed class NotificationPreference : TenantEntity
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool InApp { get; set; } = true;
    public bool Email { get; set; }

    /// <summary>For the daily "my day" email: the last time it was sent, so it goes out once a day.</summary>
    public DateTime? LastSentAt { get; set; }
}
