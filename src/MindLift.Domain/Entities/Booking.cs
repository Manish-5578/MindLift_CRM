using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>A public page where customers book a time with one person or a team (plan 7.9).</summary>
[Audited]
public sealed class BookingPage : TenantEntity
{
    /// <summary>Unique within the workspace. The public address is /book/{workspace slug}/{slug}.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public int BufferBeforeMinutes { get; set; }
    public int BufferAfterMinutes { get; set; }

    /// <summary>How far apart the offered start times are.</summary>
    public int SlotStepMinutes { get; set; } = 30;

    public int MinNoticeHours { get; set; } = 4;
    public int MaxDaysAhead { get; set; } = 30;

    /// <summary>SingleUser or RoundRobin.</summary>
    public string AssignmentMode { get; set; } = "SingleUser";

    /// <summary>JSON list of the people who take the bookings.</summary>
    public string UserIds { get; set; } = "[]";

    /// <summary>JSON: time zone, weekly opening hours and date overrides.</summary>
    public string Availability { get; set; } = "{}";

    /// <summary>JSON list of questions the customer answers.</summary>
    public string Questions { get; set; } = "[]";

    /// <summary>InPerson, Phone or Video.</summary>
    public string LocationType { get; set; } = "Video";

    public string? LocationValue { get; set; }

    /// <summary>JSON list of minutes before the start at which the customer is reminded.</summary>
    public string ReminderOffsetsMinutes { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
}

/// <summary>One time a customer booked.</summary>
public sealed class Booking : TenantEntity
{
    public Guid BookingPageId { get; set; }

    /// <summary>The meeting on the assigned person's calendar.</summary>
    public Guid ActivityId { get; set; }

    public Guid? ContactId { get; set; }
    public Guid? LeadId { get; set; }

    /// <summary>The person the booking was assigned to.</summary>
    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    /// <summary>JSON object of the answers to the page's questions.</summary>
    public string Answers { get; set; } = "{}";

    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }

    /// <summary>Confirmed, Cancelled or NoShow.</summary>
    public string Status { get; set; } = "Confirmed";

    /// <summary>SHA-256 of the secret in the customer's manage link. The secret itself is never stored.</summary>
    public string ManageTokenHash { get; set; } = string.Empty;

    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>The time zone the customer saw, used to write times in their emails.</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>JSON list of the reminder offsets already sent for the current time.</summary>
    public string RemindersSent { get; set; } = "[]";
}
