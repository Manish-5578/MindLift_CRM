using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 3 / #14: Deal Revenue Splits (deal-splits).
/// Service abstraction for managing revenue and overlay splits, validating percentages, and computing rep attribution.
/// </summary>
public interface IDealSplitService
{
    Task<DealSplitSummaryDto> GetDealSplitsAsync(Guid dealId, CancellationToken cancellationToken = default);

    Task<DealSplitSummaryDto> SetDealSplitsAsync(Guid dealId, SetDealSplitsRequest request, CancellationToken cancellationToken = default);

    Task<DealSplitMemberDto> AddOrUpdateSplitAsync(Guid dealId, SaveDealSplitMemberRequest request, CancellationToken cancellationToken = default);

    Task<DealSplitSummaryDto> RemoveSplitAsync(Guid dealId, Guid splitId, CancellationToken cancellationToken = default);

    Task<UserDealSplitsSummaryDto> GetUserSplitsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task RecalculateSplitsForDealAsync(Guid dealId, decimal newDealAmount, CancellationToken cancellationToken = default);
}
