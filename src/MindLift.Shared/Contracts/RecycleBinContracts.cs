namespace MindLift.Shared.Contracts;

public sealed record RecycleBinItemDto(
    Guid Id,
    string EntityType,
    string DisplayName,
    DateTime DeletedAtUtc,
    Guid? DeletedByUserId,
    string? DeletedByUserName,
    int DaysRemaining);

public sealed record RecycleBinQuery(
    string? EntityType = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record RecycleBinPagedResult(
    IReadOnlyList<RecycleBinItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record RestoreRecordsRequest(
    string EntityType,
    IReadOnlyList<Guid> RecordIds);

public sealed record PurgeRecordsRequest(
    string EntityType,
    IReadOnlyList<Guid> RecordIds);

public sealed record RecycleBinOperationResult(
    int ProcessedCount,
    IReadOnlyList<Guid> SuccessIds,
    IReadOnlyList<string> Errors);
