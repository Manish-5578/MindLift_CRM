namespace MindLift.Domain.Entities;

/// <summary>
/// One row per create, update, delete or sign-in of an audited entity (plan section 4.5).
/// Not a TenantEntity: audit rows are never soft-deleted, but they are still tenant-filtered.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }

    /// <summary>Cached display name of the user who performed the action.</summary>
    public string? UserName { get; set; }

    /// <summary>Cached email of the user who performed the action.</summary>
    public string? UserEmail { get; set; }

    /// <summary>The entity type name, e.g. Lead, Deal, Contact, User.</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    /// <summary>Functional module / area, e.g. Leads, Deals, Contacts, Settings, Authentication, CPQ.</summary>
    public string? Module { get; set; }

    /// <summary>Human-readable title/name of the affected record (e.g. Lead company/name, Deal title).</summary>
    public string? RecordTitle { get; set; }

    /// <summary>Create, Update, Delete, Login, Logout, etc.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Human-readable summary of what was done.</summary>
    public string? Summary { get; set; }

    /// <summary>JSON array of { field, old, new } for updates.</summary>
    public string? Changes { get; set; }

    /// <summary>Client IP address if available.</summary>
    public string? IpAddress { get; set; }

    public DateTime OccurredAt { get; set; }
}
