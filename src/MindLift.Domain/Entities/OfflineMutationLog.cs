using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Tenant-scoped log of processed offline mutations to guarantee server-side idempotency.
/// Replaying the same ClientMutationId returns the cached result without repeating CRM writes.
/// </summary>
public sealed class OfflineMutationLog : TenantEntity
{
    public string ClientMutationId { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? ServerEntityId { get; set; }
    public int? NewVersion { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultJson { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}

