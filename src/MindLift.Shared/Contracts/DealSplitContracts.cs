namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 3 / #14: Deal Revenue Splits (deal-splits).
/// Contracts for managing revenue and overlay credit splits among collaborating sales team members.
/// </summary>
public sealed record DealSplitMemberDto(
    Guid Id,
    Guid DealId,
    Guid UserId,
    string UserName,
    string UserEmail,
    string SplitType,
    decimal Percentage,
    decimal Amount,
    string? Note);

public sealed record DealSplitSummaryDto(
    Guid DealId,
    decimal DealAmount,
    string Currency,
    decimal TotalRevenuePercentage,
    decimal TotalRevenueAmount,
    bool IsValid,
    IReadOnlyList<DealSplitMemberDto> Splits);

public sealed record SaveDealSplitMemberRequest(
    Guid UserId,
    string SplitType = "Revenue",
    decimal Percentage = 100m,
    string? Note = null);

public sealed record SetDealSplitsRequest(
    IReadOnlyList<SaveDealSplitMemberRequest> Splits);

public sealed record UserDealSplitDto(
    Guid DealId,
    string DealName,
    string DealStatus,
    decimal DealAmount,
    string Currency,
    decimal Percentage,
    decimal Amount,
    string SplitType,
    DateTime? ExpectedCloseDate,
    DateTime CreatedAt);

public sealed record UserDealSplitsSummaryDto(
    Guid UserId,
    string UserName,
    string UserEmail,
    decimal TotalSplitAmount,
    int DealCount,
    IReadOnlyList<UserDealSplitDto> Splits);
