using MindLift.Domain.Common;

namespace MindLift.Domain.Common;

/// <summary>
/// Base class for every tenant-owned table. Provides the id, tenant, audit columns,
/// soft delete and the optimistic-concurrency token (mapped to PostgreSQL xmin).
/// </summary>
public abstract class TenantEntity : ITenantEntity, ISoftDelete
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency version. Starts at 1, incremented by AuditableEntityInterceptor on every update.
    /// (Plan 4.1.1 used PostgreSQL xmin; EF migrations cannot create that system column, so an explicit version is used.)
    /// </summary>
    public int Version { get; set; } = 1;
}
