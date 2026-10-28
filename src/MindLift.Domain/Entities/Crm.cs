using System.Text.Json;
using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

// Minimal record tables needed by M3 (settings). Each milestone adds the remaining columns from plan section 5.4.

[Audited]
public sealed class Lead : TenantEntity, IOwnedEntity, IHasCustomFields, ISampleData
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? CompanyName { get; set; }

    /// <summary>Lower-case, trimmed email used for duplicate checks.</summary>
    public string? NormalizedEmail { get; set; }

    /// <summary>Digits with a leading plus, used for duplicate checks.</summary>
    public string? NormalizedPhone { get; set; }

    /// <summary>Picklist value code from lead_status.</summary>
    public string Status { get; set; } = "new";

    /// <summary>Picklist value code from lead_source.</summary>
    public string? Source { get; set; }

    /// <summary>Picklist value code from lead_rating.</summary>
    public string? Rating { get; set; }

    public string? Description { get; set; }
    public string[] Tags { get; set; } = [];
    public Guid OwnerId { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? FirstContactedAt { get; set; }

    /// <summary>Picklist value code from disqualify reasons, set when the lead is disqualified.</summary>
    public string? DisqualifyReason { get; set; }

    public Guid? WebFormId { get; set; }

    public DateTime? ConvertedAt { get; set; }
    public Guid? ConvertedById { get; set; }
    public Guid? ConvertedContactId { get; set; }
    public Guid? ConvertedAccountId { get; set; }
    public Guid? ConvertedDealId { get; set; }

    public JsonDocument? CustomFields { get; set; }
    public bool IsSample { get; set; }
}
[Audited]
public sealed class Contact : TenantEntity, IOwnedEntity, IHasCustomFields, ISampleData
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? NormalizedEmail { get; set; }
    public string? NormalizedPhone { get; set; }
    public Guid? AccountId { get; set; }
    public string? JobTitle { get; set; }
    public string? Description { get; set; }

    /// <summary>Picklist value code from contact_type.</summary>
    public string? Type { get; set; }

    public Guid OwnerId { get; set; }
    public JsonDocument? CustomFields { get; set; }
    public bool IsSample { get; set; }
}

[Audited]
public sealed class Account : TenantEntity, IOwnedEntity, IHasCustomFields, ISampleData
{
    public string Name { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? Phone { get; set; }
    public string? Industry { get; set; }
    public string? Description { get; set; }

    /// <summary>The parent company when accounts form a hierarchy.</summary>
    public Guid? ParentAccountId { get; set; }

    public Guid OwnerId { get; set; }
    public JsonDocument? CustomFields { get; set; }
    public bool IsSample { get; set; }
}

[Audited]
public sealed class Deal : TenantEntity, IOwnedEntity, IHasCustomFields, ISampleData
{
    public string Name { get; set; } = string.Empty;
    public Guid PipelineId { get; set; }
    public Guid StageId { get; set; }

    /// <summary>Open, Won or Lost.</summary>
    public string Status { get; set; } = "Open";

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public int Probability { get; set; }
    public DateTime? ExpectedCloseDate { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>Picklist value code from lost_reason. Required when Status is Lost.</summary>
    public string? LostReason { get; set; }

    public string? LostNotes { get; set; }
    public string? Description { get; set; }
    public Guid? PrimaryContactId { get; set; }

    /// <summary>When the deal entered its current stage. Used for days-in-stage and rotting.</summary>
    public DateTime StageChangedAt { get; set; }

    /// <summary>True when someone typed a probability, so moving stages no longer resets it.</summary>
    public bool ProbabilityOverridden { get; set; }

    public DateTime? LastActivityAt { get; set; }
    public DateTime? NextActivityAt { get; set; }
    public Guid? AccountId { get; set; }
    public Guid OwnerId { get; set; }
    public JsonDocument? CustomFields { get; set; }
    public bool IsSample { get; set; }

    public ICollection<DealLineItem> LineItems { get; set; } = new List<DealLineItem>();
}

[Audited]
public sealed class Activity : TenantEntity, IOwnedEntity, IHasCustomFields, ISampleData
{
    /// <summary>Task, Call, Meeting or Note.</summary>
    public string Type { get; set; } = "Task";

    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public DateTime? DueAt { get; set; }

    /// <summary>Picklist value code from call_outcome (calls only).</summary>
    public string? CallOutcome { get; set; }

    public string? Description { get; set; }

    /// <summary>Low, Normal or High.</summary>
    public string Priority { get; set; } = "Normal";

    /// <summary>Calls: Inbound or Outbound.</summary>
    public string? Direction { get; set; }

    public DateTime? EndAt { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Location { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedById { get; set; }

    /// <summary>Minutes before DueAt to remind. Delivery arrives with notifications.</summary>
    public int? ReminderMinutes { get; set; }

    /// <summary>The task this one was created from by Complete and follow up.</summary>
    public Guid? ParentActivityId { get; set; }

    /// <summary>Set on tasks created by an onboarding checklist.</summary>
    public Guid? ChecklistTemplateId { get; set; }

    /// <summary>JSON list of external attendee email addresses (meetings).</summary>
    public string Attendees { get; set; } = "[]";

    /// <summary>When calendar invitations were last sent, so a change can be sent as an update.</summary>
    public DateTime? InvitationsSentAt { get; set; }

    /// <summary>Set when the "due soon" reminder was delivered, so it goes out once.</summary>
    public DateTime? ReminderSentAt { get; set; }

    /// <summary>Set when the overdue notification was delivered, so it goes out once.</summary>
    public DateTime? OverdueNotifiedAt { get; set; }

    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
    public Guid OwnerId { get; set; }
    public JsonDocument? CustomFields { get; set; }
    public bool IsSample { get; set; }
}

// ---------- Pipelines ----------

[Audited]
public sealed class Pipeline : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string Origin { get; set; } = "User";
}

[Audited]
public sealed class PipelineStage : TenantEntity
{
    public Guid PipelineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public int SortOrder { get; set; }

    /// <summary>0 to 100.</summary>
    public int Probability { get; set; }

    /// <summary>Open, Won or Lost.</summary>
    public string StageType { get; set; } = "Open";

    public string? Color { get; set; }

    /// <summary>Deals with no activity for this many days are flagged as rotting.</summary>
    public int? RottingDays { get; set; }
    public string Origin { get; set; } = "User";
}

// ---------- Custom fields and layouts ----------

[Audited]
public sealed class CustomFieldDefinition : TenantEntity
{
    /// <summary>Registry key of the entity: leads, contacts, accounts, deals or activities.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>cf_ + snake_case. Immutable after creation.</summary>
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string? LabelAr { get; set; }
    public string FieldType { get; set; } = "Text";
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowInList { get; set; }
    public bool Filterable { get; set; } = true;
    public string? Section { get; set; }
    public int SortOrder { get; set; }
    public string Origin { get; set; } = "User";

    /// <summary>JSON array of { value, label, labelAr, color } for picklist fields.</summary>
    public JsonDocument? Options { get; set; }
}

[Audited]
public sealed class FieldLabelOverride : TenantEntity
{
    public string EntityType { get; set; } = string.Empty;
    public string FieldKey { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? LabelAr { get; set; }
    public bool IsHidden { get; set; }
}

[Audited]
public sealed class Layout : TenantEntity
{
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Form or Detail.</summary>
    public string Kind { get; set; } = "Form";

    /// <summary>JSON array of { section, sectionAr, columns, fields }.</summary>
    public JsonDocument Definition { get; set; } = JsonDocument.Parse("[]");
    public string Origin { get; set; } = "User";
}

[Audited]
public sealed class SavedView : TenantEntity
{
    public string EntityType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? OwnerUserId { get; set; }
    public bool IsShared { get; set; }
    public bool IsDefault { get; set; }
    public JsonDocument? Filter { get; set; }
    public string Columns { get; set; } = "[]";
    public string? SortField { get; set; }
    public string? SortDir { get; set; }
}

[Audited]
public sealed class ExchangeRate : TenantEntity
{
    public string Currency { get; set; } = string.Empty;
    public decimal RateToBase { get; set; }
    public DateTime EffectiveDate { get; set; }
}






/// <summary>A person involved in a deal, with the part they play. One of them can be the primary contact.</summary>
public sealed class DealContact : TenantEntity
{
    public Guid DealId { get; set; }
    public Guid ContactId { get; set; }
    public string? Role { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>One move of a deal from a stage to another.</summary>
public sealed class DealStageHistory : TenantEntity
{
    public Guid DealId { get; set; }
    public Guid? FromStageId { get; set; }
    public Guid ToStageId { get; set; }
    public DateTime ChangedAt { get; set; }
    public Guid? ChangedById { get; set; }

    /// <summary>How long the deal sat in the stage it left.</summary>
    public long? SecondsInFromStage { get; set; }
}

/// <summary>A set of tasks created automatically when something happens, for example when a deal is won.</summary>
[Audited]
public sealed class ChecklistTemplate : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>What starts it. Only DealWon exists for now.</summary>
    public string Trigger { get; set; } = "DealWon";

    /// <summary>Limits the template to one pipeline. Empty means every pipeline.</summary>
    public Guid? PipelineId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>JSON list of { subject, offsetDays, description }.</summary>
    public string Items { get; set; } = "[]";
}

/// <summary>A person tagged in a note with an @mention. Notifications for it arrive with M10.</summary>
public sealed class ActivityMention : TenantEntity
{
    public Guid ActivityId { get; set; }
    public Guid UserId { get; set; }
}
