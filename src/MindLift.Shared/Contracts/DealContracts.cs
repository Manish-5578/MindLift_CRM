using System.Text.Json;

namespace MindLift.Shared.Contracts;

public sealed record DealListItemDto(
    Guid Id,
    string Name,
    decimal Amount,
    string Currency,
    decimal AmountInBase,
    Guid PipelineId,
    Guid StageId,
    string StageName,
    string StageType,
    string Status,
    int Probability,
    Guid? AccountId,
    string? AccountName,
    string? PrimaryContactName,
    Guid OwnerId,
    string? OwnerName,
    DateTime? ExpectedCloseDate,
    bool IsRotting,
    DateTime CreatedAtUtc,
    string? StageNameAr = null);

public sealed record DealContactDto(Guid ContactId, string FullName, string? Role, bool IsPrimary);

public sealed record DealContactInput(Guid ContactId, string? Role, bool IsPrimary);

public sealed record DealDto(
    Guid Id,
    string Name,
    Guid PipelineId,
    Guid StageId,
    string Status,
    decimal Amount,
    string Currency,
    decimal AmountInBase,
    int Probability,
    bool ProbabilityOverridden,
    DateTime? ExpectedCloseDate,
    DateTime? ClosedAtUtc,
    string? LostReason,
    string? LostNotes,
    string? Description,
    Guid? AccountId,
    string? AccountName,
    IReadOnlyList<DealContactDto> Contacts,
    Guid OwnerId,
    DateTime CreatedAtUtc,
    DateTime StageChangedAtUtc,
    bool IsRotting,
    int Version,
    IReadOnlyDictionary<string, JsonElement> CustomFields);

public sealed record SaveDealRequest(
    string Name,
    Guid? PipelineId,
    Guid? StageId,
    decimal Amount,
    string? Currency,
    int? Probability,
    DateTime? ExpectedCloseDate,
    Guid? AccountId,
    IReadOnlyList<DealContactInput>? Contacts,
    string? Description,
    Guid? OwnerId,
    IReadOnlyDictionary<string, JsonElement>? CustomFields);

public sealed record ChangeDealStageRequest(Guid StageId, string? LostReason, string? LostNotes, DateTime? ClosedAt, int? Version);

public sealed record DealCardDto(
    Guid Id,
    string Name,
    decimal Amount,
    string Currency,
    decimal AmountInBase,
    string? AccountName,
    string? ContactName,
    Guid OwnerId,
    string? OwnerName,
    DateTime? ExpectedCloseDate,
    bool IsRotting,
    int DaysInStage,
    int Version);

public sealed record DealBoardStageDto(
    Guid Id,
    string Name,
    string Type,
    string? Color,
    int Probability,
    int Count,
    decimal TotalInBase,
    decimal WeightedInBase,
    IReadOnlyList<DealCardDto> Deals,
    string? NameAr = null);

public sealed record DealBoardDto(Guid PipelineId, string BaseCurrency, IReadOnlyList<DealBoardStageDto> Stages);

public sealed record DealStageHistoryDto(string? FromStage, string ToStage, DateTime ChangedAtUtc, string? ChangedBy, double? DaysInFromStage, string? FromStageAr = null, string? ToStageAr = null);
