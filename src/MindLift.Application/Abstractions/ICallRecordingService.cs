using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ICallRecordingService
{
    Task<RecordingConsentDto> GetConsentSettingsAsync(CancellationToken ct = default);
    Task<RecordingConsentDto> UpdateConsentSettingsAsync(UpdateRecordingConsentRequest request, CancellationToken ct = default);
    Task<CallRecordingDto> GetRecordingAsync(Guid callId, CancellationToken ct = default);
    Task<TranscriptionResultDto> TranscribeCallAsync(Guid callId, TranscribeCallRequest? request = null, CancellationToken ct = default);
    Task<TranscriptionResultDto> GetTranscriptionAsync(Guid callId, CancellationToken ct = default);
    Task<bool> ProcessTranscriptionWebhookAsync(TranscriptionWebhookRequest request, CancellationToken ct = default);
    Task<GdprDeleteRecordingResult> DeleteRecordingAsync(Guid callId, CancellationToken ct = default);
}
