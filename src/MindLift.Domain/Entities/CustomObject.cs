using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 2 / #11: Custom Object Builder (custom-objects).
/// Definition for a user-created business entity (e.g. Properties, Vehicles, Projects).
/// </summary>
[Audited]
public sealed class CustomObjectDefinition : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string PluralName { get; set; } = string.Empty;

    /// <summary>Unique URL/API slug per tenant (e.g. "properties", "vehicles").</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Icon { get; set; }

    /// <summary>The field key in record data used as primary label/title (e.g. "title", "vin", "property_name").</summary>
    public string TitleField { get; set; } = "name";

    public bool IsActive { get; set; } = true;

    /// <summary>Serialized JSON array of CustomObjectFieldDto describing the entity's schema.</summary>
    public string FieldsJson { get; set; } = "[]";
}

/// <summary>
/// Phase 2 / #11: Custom Object Builder (custom-objects).
/// An individual record of a custom object definition with dynamic JSON storage and auto-incrementing record number.
/// </summary>
[Audited]
public sealed class CustomObjectRecord : TenantEntity
{
    public Guid ObjectDefinitionId { get; set; }

    /// <summary>Auto-incrementing record index per tenant and object definition (e.g. 1, 2, 3...).</summary>
    public int RecordNumber { get; set; }

    /// <summary>Computed primary display title for listings, search, and relations.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Dynamic record payload matching the definition's fields schema.</summary>
    public string DataJson { get; set; } = "{}";
}
