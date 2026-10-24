using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// An email sent to many people at once: who gets it (leads or contacts, narrowed by a few filters), what it says, and how it did.
/// Status moves draft, scheduled, sending, sent (or cancelled).
/// </summary>
public sealed class Campaign : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    /// <summary>The message as plain text. {{first_name}}, {{last_name}} and {{company}} are replaced for each person.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>"draft", "scheduled", "sending", "sent" or "cancelled".</summary>
    public string Status { get; set; } = "draft";

    /// <summary>"leads" or "contacts".</summary>
    public string AudienceType { get; set; } = "leads";

    /// <summary>Leads only: the lead status code, source code and tag a lead must have. Empty means any.</summary>
    public string? AudienceStatus { get; set; }
    public string? AudienceSource { get; set; }
    public string? AudienceTag { get; set; }

    /// <summary>Contacts only: the contact type.</summary>
    public string? AudienceContactType { get; set; }

    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Set when sending stopped because the company reached its daily email limit; the rest goes out on a later day.</summary>
    public DateTime? PausedAt { get; set; }
}

/// <summary>One person a campaign goes to, fixed when the campaign starts, with what happened to their copy.</summary>
public sealed class CampaignRecipient : TenantEntity
{
    public Guid CampaignId { get; set; }

    /// <summary>"lead" or "contact".</summary>
    public string RecordType { get; set; } = "lead";

    public Guid RecordId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Company { get; set; }

    /// <summary>"pending", "sent", "failed" or "skipped".</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Why it was skipped or failed.</summary>
    public string? Reason { get; set; }

    /// <summary>Names this person's copy in the tracking links, so an open or click is recorded against them.</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime? SentAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClickedAt { get; set; }
    public DateTime? UnsubscribedAt { get; set; }
    public int OpenCount { get; set; }
    public int ClickCount { get; set; }
}
