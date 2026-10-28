using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 5 / #36: Meeting booking links (meeting-links).
/// Personalized, shareable meeting booking links tied to reps, contacts, leads, and deals.
/// </summary>
[Audited]
public sealed class MeetingLink : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid? BookingPageId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public string LocationType { get; set; } = "Video"; // "Video", "Phone", "InPerson"
    public string? LocationValue { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? DealId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public bool IsSingleUse { get; set; }
    public bool IsUsed { get; set; }
    public bool IsActive { get; set; } = true;
    public int ClickCount { get; set; }
    public int BookingCount { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string ShareableUrl { get; set; } = string.Empty;
}
