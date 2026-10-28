using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ISlaService
{
    // Policy Management
    Task<SlaPolicyDto> CreatePolicyAsync(SaveSlaPolicyRequest req, CancellationToken ct = default);
    Task<SlaPolicyDto> GetPolicyByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SlaPolicyDto>> ListPoliciesAsync(bool? activeOnly = null, CancellationToken ct = default);
    Task<SlaPolicyDto> UpdatePolicyAsync(Guid id, SaveSlaPolicyRequest req, CancellationToken ct = default);
    Task<bool> DeletePolicyAsync(Guid id, CancellationToken ct = default);

    // SLA Tracking & Escalation
    Task<LeadSlaTrackerDto?> AttachSlaToLeadAsync(Guid leadId, CancellationToken ct = default);
    Task<LeadSlaTrackerDto> RecordFirstResponseAsync(Guid leadId, RecordResponseRequest? req = null, CancellationToken ct = default);
    Task<LeadSlaTrackerDto?> GetSlaForLeadAsync(Guid leadId, CancellationToken ct = default);
    Task<SlaCheckResultDto> ProcessSlaEscalationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LeadSlaTrackerDto>> ListTrackersAsync(string? status = null, bool? escalated = null, int limit = 50, CancellationToken ct = default);
}
