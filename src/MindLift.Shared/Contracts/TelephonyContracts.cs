namespace MindLift.Shared.Contracts;

public sealed record TelephonySettingsDto(
    string Provider,
    string? AccountSid,
    string CallerId,
    bool IsActive,
    bool AutoLogCalls,
    bool RecordCallsDefault,
    bool WebRtcEnabled);

public sealed record UpdateTelephonySettingsRequest(
    string? Provider,
    string? AccountSid,
    string? AuthToken,
    string? CallerId,
    bool IsActive,
    bool AutoLogCalls,
    bool RecordCallsDefault,
    bool WebRtcEnabled);

public sealed record InitiateCallRequest(
    string ToNumber,
    string? FromNumber = null,
    Guid? LeadId = null,
    Guid? ContactId = null,
    Guid? AccountId = null,
    Guid? DealId = null,
    string? Notes = null,
    bool? Record = null);

public sealed record InitiateCallResult(
    bool Success,
    Guid CallId,
    string CallSid,
    string Status,
    string FromNumber,
    string ToNumber,
    string? WebRtcToken,
    string? Message);

public sealed record UpdateCallStatusRequest(
    string CallSid,
    string Status,
    int? DurationSeconds = null,
    string? RecordingUrl = null);

public sealed record UpdateCallDispositionRequest(
    string Disposition,
    string? Notes);

public sealed record CallSessionDto(
    Guid Id,
    Guid UserId,
    string CallSid,
    string FromNumber,
    string ToNumber,
    string Direction,
    string Status,
    DateTime StartedAtUtc,
    DateTime? AnsweredAtUtc,
    DateTime? EndedAtUtc,
    int DurationSeconds,
    Guid? LeadId,
    Guid? ContactId,
    Guid? AccountId,
    Guid? DealId,
    string? Notes,
    string? Disposition,
    string? RecordingUrl,
    string? TranscriptionText);

public sealed record CallHistoryQuery(
    Guid? LeadId = null,
    Guid? ContactId = null,
    Guid? AccountId = null,
    Guid? DealId = null,
    Guid? UserId = null,
    string? Status = null,
    int Limit = 50);
