using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ICustomerHealthService
{
    Task<CustomerSurveyDto> SubmitSurveyAsync(SubmitSurveyRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerSurveyDto>> GetSurveysAsync(string? type = null, Guid? accountId = null, Guid? contactId = null, int limit = 100, CancellationToken ct = default);
    Task<SurveySummaryDto> GetSurveySummaryAsync(string? type = null, Guid? accountId = null, CancellationToken ct = default);
    Task<AccountHealthScoreDto> GetAccountHealthAsync(Guid accountId, CancellationToken ct = default);
    Task<PortfolioHealthOverviewDto> GetPortfolioOverviewAsync(CancellationToken ct = default);
}
