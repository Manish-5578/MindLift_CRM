namespace MindLift.Shared.Contracts;

/// <summary>
/// Request to change an account's parent in the hierarchy.
/// </summary>
public sealed record SetAccountParentRequest(Guid? ParentAccountId);

/// <summary>
/// Aggregated business metrics across an account subtree.
/// </summary>
public sealed record AccountHierarchyMetricsDto(
    int SubtreeAccountCount,
    int DirectChildCount,
    int TotalContactsCount,
    int OpenDealsCount,
    decimal OpenDealsValue,
    int WonDealsCount,
    decimal WonDealsValue,
    int OpenTicketsCount);

/// <summary>
/// A node in the account hierarchy tree.
/// </summary>
public sealed record AccountHierarchyNodeDto(
    Guid Id,
    string Name,
    string? Industry,
    string? Website,
    Guid OwnerId,
    string? OwnerName,
    Guid? ParentAccountId,
    int Level,
    AccountHierarchyMetricsDto Metrics,
    IReadOnlyList<AccountHierarchyNodeDto> Children);

/// <summary>
/// The complete hierarchy tree response rooted at the top ancestor.
/// </summary>
public sealed record AccountHierarchyTreeDto(
    AccountHierarchyNodeDto Root,
    Guid FocusAccountId,
    AccountHierarchyMetricsDto TotalRollup);
