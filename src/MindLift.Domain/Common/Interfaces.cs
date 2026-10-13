using System.Text.Json;

namespace MindLift.Domain.Common;

/// <summary>Every tenant-owned record belongs to exactly one tenant (plan section 4.1).</summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

/// <summary>Soft-deleted records stay in the database and are hidden by the global query filter.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

/// <summary>Records with an owner (lead, contact, account, deal, activity). The owner drives data scope.</summary>
public interface IOwnedEntity
{
    Guid OwnerId { get; set; }
}

/// <summary>Records that accept client-defined custom fields stored as jsonb (plan section 4.6.2).</summary>
public interface IHasCustomFields
{
    JsonDocument? CustomFields { get; set; }
}

/// <summary>Records created by the sample-data loader; removable in one step (plan section 6.7).</summary>
public interface ISampleData
{
    bool IsSample { get; set; }
}
