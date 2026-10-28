namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 6 / #42: Leaderboards (leaderboards).
/// Gamified sales rep rankings, quota attainment, and podium performance tiers.
/// </summary>
public sealed record LeaderboardQueryRequest(
    string Metric = "revenue",
    string Period = "this_month",
    Guid? TeamId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public sealed record LeaderboardResponseDto(
    string Metric,
    string Period,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    string Currency,
    IReadOnlyList<LeaderboardEntryDto> Podium,
    IReadOnlyList<LeaderboardEntryDto> Entries,
    CurrentUserRankDto? MyRank);

public sealed record LeaderboardEntryDto(
    int Rank,
    Guid UserId,
    string UserName,
    string? UserEmail,
    decimal Score,
    string FormattedScore,
    decimal? TargetAmount,
    decimal? AttainmentPercentage,
    string Badge,
    string Tier,
    int RankChange);

public sealed record CurrentUserRankDto(
    int Rank,
    decimal Score,
    string FormattedScore,
    decimal? GapToNextRank,
    string? NextRankUserName);

public sealed record LeaderboardMetaDto(
    IReadOnlyList<string> Metrics,
    IReadOnlyList<string> Periods);
