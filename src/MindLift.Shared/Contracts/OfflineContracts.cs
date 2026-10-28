using System.Text.Json;

namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 6 / #43: Offline mode (PWA) (pwa-offline).
/// Contracts for PWA manifest, offline bootstrap snapshots, delta synchronization,
/// and offline mutation batch synchronization with conflict detection.
/// </summary>
public sealed record OfflineRecordDto(
    Guid Id,
    string EntityType,
    string Title,
    string? Subtitle,
    decimal? Amount,
    string? Status,
    int Version,
    DateTime UpdatedAtUtc,
    JsonDocument? Data);

public sealed record OfflineBootstrapDto(
    DateTime SnapshotUtc,
    string BaseCurrency,
    IReadOnlyList<PipelineDto> Pipelines,
    IReadOnlyList<OfflineRecordDto> Records);

public sealed record OfflineDeltaDto(
    DateTime ServerTimestampUtc,
    IReadOnlyList<OfflineRecordDto> Upserts,
    IReadOnlyList<Guid> DeletedIds);

public sealed record OfflineMutationRequest(
    string ClientMutationId,
    string EntityType,
    string Operation,
    Guid? EntityId,
    string? ClientTempId,
    JsonDocument Payload,
    int? BaseVersion);

public sealed record OfflineSyncBatchRequest(
    string BatchId,
    IReadOnlyList<OfflineMutationRequest> Mutations);

public sealed record OfflineMutationResultDto(
    string ClientMutationId,
    string Status,
    Guid? ServerEntityId,
    string? ErrorMessage,
    int? NewVersion);

public sealed record OfflineSyncBatchResultDto(
    string BatchId,
    int ProcessedCount,
    int AppliedCount,
    int ConflictCount,
    int ErrorCount,
    IReadOnlyDictionary<string, Guid> IdMap,
    IReadOnlyList<OfflineMutationResultDto> Results);

public sealed record PwaManifestDto(
    string Name,
    string ShortName,
    string StartUrl,
    string Display,
    string ThemeColor,
    string BackgroundColor,
    IReadOnlyList<PwaIconDto> Icons);

public sealed record PwaIconDto(
    string Src,
    string Sizes,
    string Type,
    string Purpose);
