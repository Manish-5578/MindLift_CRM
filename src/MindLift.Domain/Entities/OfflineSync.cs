using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 6 / #43: Offline mode (PWA) (pwa-offline).
/// Audit log of offline sync batches submitted by clients upon reconnecting.
/// </summary>
public sealed class OfflineSyncBatchLog : TenantEntity
{
    public Guid UserId { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public int ClientMutationCount { get; set; }
    public int AppliedCount { get; set; }
    public int ConflictCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}
