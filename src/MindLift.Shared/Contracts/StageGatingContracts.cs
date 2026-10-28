namespace MindLift.Shared.Contracts;

public sealed record DealStageGateDto(
    Guid Id,
    Guid PipelineId,
    Guid StageId,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<string> RequiredFields,
    decimal? MinAmount,
    bool RequireLineItems,
    RuleNode? Condition,
    IReadOnlyList<string> AllowedRoles,
    string? FailureMessage);

public sealed record SaveDealStageGateRequest(
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<string>? RequiredFields,
    decimal? MinAmount,
    bool RequireLineItems,
    RuleNode? Condition,
    IReadOnlyList<string>? AllowedRoles,
    string? FailureMessage);
