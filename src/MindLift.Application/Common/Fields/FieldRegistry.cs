using MindLift.Domain.Entities;
using MindLift.Shared.Filtering;

namespace MindLift.Application.Common.Fields;

/// <summary>
/// One field of an entity (plan 4.6.1). System fields point at a CLR property; custom fields read the jsonb document.
/// </summary>
public sealed record FieldDef(
    string Key,
    string Label,
    string? LabelAr,
    FieldType Type,
    bool IsCustom,
    bool IsRequired,
    bool Filterable,
    string? PicklistKey,
    string? PropertyName);

/// <summary>An entity known to the registry, for example "leads" → <see cref="Lead"/>.</summary>
public sealed record EntityInfo(string Key, string CustomEntityType, Type ClrType, IReadOnlyList<FieldDef> SystemFields);

/// <summary>Declares every system field of the Phase 1 record entities (plan 4.6.1, 5.4). Adding a field means adding a line here.</summary>
public static class SystemFields
{
    private static FieldDef F(string key, string label, FieldType type, string property, bool required = false, string? picklist = null, bool filterable = true)
        => new(key, label, null, type, false, required, filterable, picklist, property);

    public static readonly IReadOnlyList<EntityInfo> Entities =
    [
        new("leads", "Lead", typeof(Lead),
        [
            F("first_name", "First name", FieldType.Text, nameof(Lead.FirstName), required: true),
            F("last_name", "Last name", FieldType.Text, nameof(Lead.LastName)),
            F("email", "Email", FieldType.Email, nameof(Lead.Email)),
            F("phone", "Phone", FieldType.Phone, nameof(Lead.Phone)),
            F("company_name", "Company", FieldType.Text, nameof(Lead.CompanyName)),
            F("status", "Status", FieldType.Picklist, nameof(Lead.Status), picklist: "lead_status"),
            F("source", "Source", FieldType.Picklist, nameof(Lead.Source), picklist: "lead_source"),
            F("rating", "Rating", FieldType.Picklist, nameof(Lead.Rating), picklist: "lead_rating"),
            F("owner_id", "Owner", FieldType.User, nameof(Lead.OwnerId)),
            F("created_at", "Created", FieldType.DateTime, nameof(Lead.CreatedAt)),
        ]),
        new("contacts", "Contact", typeof(Contact),
        [
            F("first_name", "First name", FieldType.Text, nameof(Contact.FirstName), required: true),
            F("last_name", "Last name", FieldType.Text, nameof(Contact.LastName)),
            F("email", "Email", FieldType.Email, nameof(Contact.Email)),
            F("phone", "Phone", FieldType.Phone, nameof(Contact.Phone)),
            F("job_title", "Job title", FieldType.Text, nameof(Contact.JobTitle)),
            F("account_id", "Company", FieldType.Lookup, nameof(Contact.AccountId)),
            F("type", "Type", FieldType.Picklist, nameof(Contact.Type), picklist: "contact_type"),
            F("owner_id", "Owner", FieldType.User, nameof(Contact.OwnerId)),
            F("created_at", "Created", FieldType.DateTime, nameof(Contact.CreatedAt)),
        ]),
        new("accounts", "Account", typeof(Account),
        [
            F("name", "Name", FieldType.Text, nameof(Account.Name), required: true),
            F("website", "Website", FieldType.Url, nameof(Account.Website)),
            F("phone", "Phone", FieldType.Phone, nameof(Account.Phone)),
            F("industry", "Industry", FieldType.Text, nameof(Account.Industry)),
            F("owner_id", "Owner", FieldType.User, nameof(Account.OwnerId)),
            F("created_at", "Created", FieldType.DateTime, nameof(Account.CreatedAt)),
        ]),
        new("deals", "Deal", typeof(Deal),
        [
            F("name", "Name", FieldType.Text, nameof(Deal.Name), required: true),
            F("pipeline_id", "Pipeline", FieldType.Lookup, nameof(Deal.PipelineId)),
            F("stage_id", "Stage", FieldType.Lookup, nameof(Deal.StageId)),
            F("status", "Status", FieldType.Text, nameof(Deal.Status)),
            F("amount", "Amount", FieldType.Currency, nameof(Deal.Amount)),
            F("currency", "Currency", FieldType.Text, nameof(Deal.Currency)),
            F("probability", "Probability", FieldType.Percent, nameof(Deal.Probability)),
            F("expected_close_date", "Expected close", FieldType.Date, nameof(Deal.ExpectedCloseDate)),
            F("lost_reason", "Lost reason", FieldType.Picklist, nameof(Deal.LostReason), picklist: "lost_reason"),
            F("account_id", "Account", FieldType.Lookup, nameof(Deal.AccountId)),
            F("owner_id", "Owner", FieldType.User, nameof(Deal.OwnerId)),
            F("created_at", "Created", FieldType.DateTime, nameof(Deal.CreatedAt)),
            F("closed_at", "Closed", FieldType.DateTime, nameof(Deal.ClosedAt)),
        ]),
        new("activities", "Activity", typeof(Activity),
        [
            F("type", "Type", FieldType.Text, nameof(Activity.Type), required: true),
            F("subject", "Subject", FieldType.Text, nameof(Activity.Subject)),
            F("status", "Status", FieldType.Text, nameof(Activity.Status)),
            F("due_at", "Due", FieldType.DateTime, nameof(Activity.DueAt)),
            F("call_outcome", "Call outcome", FieldType.Picklist, nameof(Activity.CallOutcome), picklist: "call_outcome"),
            F("lead_id", "Lead", FieldType.Lookup, nameof(Activity.LeadId)),
            F("contact_id", "Contact", FieldType.Lookup, nameof(Activity.ContactId)),
            F("account_id", "Account", FieldType.Lookup, nameof(Activity.AccountId)),
            F("deal_id", "Deal", FieldType.Lookup, nameof(Activity.DealId)),
            F("owner_id", "Owner", FieldType.User, nameof(Activity.OwnerId)),
            F("created_at", "Created", FieldType.DateTime, nameof(Activity.CreatedAt)),
        ]),
    ];

    public static EntityInfo Get(string entityKey)
        => Entities.FirstOrDefault(e => e.Key == entityKey)
           ?? throw new MindLift.Application.Common.Exceptions.NotFoundException("Entity", entityKey);

    /// <summary>Maps the CLR-style entity name used by custom fields ("Lead") to the registry key ("leads").</summary>
    public static EntityInfo FromCustomEntityType(string customEntityType)
        => Entities.FirstOrDefault(e => string.Equals(e.CustomEntityType, customEntityType, StringComparison.OrdinalIgnoreCase))
           ?? throw new MindLift.Application.Common.Exceptions.NotFoundException("Entity", customEntityType);
}
