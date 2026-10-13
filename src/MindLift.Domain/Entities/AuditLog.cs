namespace MindLift.Domain.Entities;

/// <summary>
/// One row per create, update or delete of an audited entity (plan section 4.5).
/// Not a TenantEntity: audit rows are never soft-deleted, but they are still tenant-filtered.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    /// <summary>Create, Update or Delete.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>JSON array of { field, old, new } for updates.</summary>
    public string? Changes { get; set; }

    public DateTime OccurredAt { get; set; }
}
