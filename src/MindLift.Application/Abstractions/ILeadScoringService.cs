using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ILeadScoringService
{
    // Rule Management
    Task<LeadScoringRuleDto> CreateRuleAsync(SaveLeadScoringRuleRequest req, CancellationToken ct = default);
    Task<LeadScoringRuleDto> GetRuleByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LeadScoringRuleDto>> ListRulesAsync(bool? activeOnly = null, CancellationToken ct = default);
    Task<LeadScoringRuleDto> UpdateRuleAsync(Guid id, SaveLeadScoringRuleRequest req, CancellationToken ct = default);
    Task<bool> DeleteRuleAsync(Guid id, CancellationToken ct = default);

    // Scoring Evaluation
    Task<LeadScoreResultDto> EvaluateLeadScoreAsync(Guid leadId, CancellationToken ct = default);
    Task<BatchScoreResultDto> BatchEvaluateScoresAsync(BatchScoreRequest? req = null, CancellationToken ct = default);
    Task<IReadOnlyList<LeadScoreLogDto>> GetScoreLogsAsync(Guid leadId, CancellationToken ct = default);
}
