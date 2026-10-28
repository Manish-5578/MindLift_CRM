using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ITelephonyService
{
    Task<TelephonySettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<TelephonySettingsDto> UpdateSettingsAsync(UpdateTelephonySettingsRequest request, CancellationToken ct = default);
    Task<InitiateCallResult> InitiateCallAsync(InitiateCallRequest request, Guid userId, CancellationToken ct = default);
    Task<CallSessionDto> GetCallAsync(Guid id, CancellationToken ct = default);
    Task<CallSessionDto> UpdateCallStatusAsync(UpdateCallStatusRequest request, CancellationToken ct = default);
    Task<CallSessionDto> SetDispositionAsync(Guid callId, UpdateCallDispositionRequest request, CancellationToken ct = default);
    Task<CallSessionDto> EndCallAsync(Guid callId, int? durationSeconds = null, CancellationToken ct = default);
    Task<IReadOnlyList<CallSessionDto>> GetCallHistoryAsync(CallHistoryQuery query, CancellationToken ct = default);
}
