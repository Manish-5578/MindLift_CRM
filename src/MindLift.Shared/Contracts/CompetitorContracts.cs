namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 3 / #17: Competitor Tracking (competitors).
/// Contracts for competitor battlecards, deal associations, threat levels, and competitive win-rate analytics.
/// </summary>
public sealed record CompetitorDto(
    Guid Id,
    string Name,
    string? Website,
    string? Strengths,
    string? Weaknesses,
    string? Battlecard,
    string? PricingStrategy,
    bool IsActive,
    int TotalDealsCount,
    int WonDealsCount,
    int LostDealsCount,
    decimal WinRatePercentage,
    decimal WonRevenue,
    decimal LostRevenue);

public sealed record SaveCompetitorRequest(
    string Name,
    string? Website = null,
    string? Strengths = null,
    string? Weaknesses = null,
    string? Battlecard = null,
    string? PricingStrategy = null,
    bool IsActive = true);

public sealed record DealCompetitorDto(
    Guid DealId,
    Guid CompetitorId,
    string CompetitorName,
    string? Website,
    string ThreatLevel,
    string? StrengthsObserved,
    string? WeaknessesObserved,
    string? Notes,
    DateTime CreatedAtUtc);

public sealed record AddDealCompetitorRequest(
    Guid CompetitorId,
    string ThreatLevel = "Medium",
    string? StrengthsObserved = null,
    string? WeaknessesObserved = null,
    string? Notes = null);

public sealed record UpdateDealCompetitorRequest(
    string ThreatLevel,
    string? StrengthsObserved = null,
    string? WeaknessesObserved = null,
    string? Notes = null);

public sealed record CompetitorAnalyticsSummaryDto(
    int TotalCompetitors,
    int TotalDealsWithCompetitors,
    decimal OverallWinRateAgainstCompetitors,
    IReadOnlyList<CompetitorDto> Competitors);
