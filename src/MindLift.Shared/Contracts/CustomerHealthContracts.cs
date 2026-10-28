namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 6 / #38: Customer health, NPS and CSAT (customer-health).
/// Request to submit an inbound customer survey response.
/// </summary>
public sealed record SubmitSurveyRequest(
    string Type,
    int Score,
    string? Comment = null,
    string? CustomerName = null,
    string? CustomerEmail = null,
    string? CustomerPhone = null,
    Guid? AccountId = null,
    Guid? ContactId = null,
    Guid? TicketId = null,
    Guid? DealId = null,
    string? Channel = null);

/// <summary>
/// A recorded customer survey response.
/// </summary>
public sealed record CustomerSurveyDto(
    Guid Id,
    string Type,
    int Score,
    string Sentiment,
    string? Comment,
    string Channel,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    Guid? AccountId,
    string? AccountName,
    Guid? ContactId,
    string? ContactName,
    Guid? TicketId,
    Guid? DealId,
    DateTime SubmittedAtUtc);

/// <summary>
/// Aggregate satisfaction metrics (NPS / CSAT).
/// </summary>
public sealed record SurveySummaryDto(
    string Type,
    int TotalResponses,
    decimal AverageScore,
    int PromotersCount,
    int PassivesCount,
    int DetractorsCount,
    decimal NpsScore,
    int SatisfiedCount,
    int NeutralCount,
    int DissatisfiedCount,
    decimal CsatPercentage);

/// <summary>
/// Detailed health evaluation for a specific account.
/// </summary>
public sealed record AccountHealthScoreDto(
    Guid AccountId,
    string AccountName,
    int HealthScore,
    string HealthStatus,
    string Trend,
    int NpsScoreComponent,
    int SupportHealthComponent,
    int EngagementComponent,
    int DealMomentumComponent,
    int TotalSurveysCount,
    decimal? AverageNps,
    int OpenTicketsCount,
    int UrgentTicketsCount,
    int RecentActivitiesCount,
    int WonDealsCount,
    decimal WonDealsValue,
    decimal OpenDealsValue,
    IReadOnlyList<string> RiskFactors,
    IReadOnlyList<string> Recommendations,
    DateTime CalculatedAtUtc);

/// <summary>
/// High-level portfolio risk and health overview across all customer accounts.
/// </summary>
public sealed record PortfolioHealthOverviewDto(
    int TotalAccounts,
    int HealthyCount,
    int AverageCount,
    int AtRiskCount,
    decimal AverageHealthScore,
    IReadOnlyList<AccountHealthScoreDto> TopAtRiskAccounts);
