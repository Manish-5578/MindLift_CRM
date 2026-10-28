namespace MindLift.Shared.Contracts;

public sealed record RecordingConsentDto(
    string ConsentMode,
    bool DualPartyNotification,
    int RetentionDays);

public sealed record UpdateRecordingConsentRequest(
    string ConsentMode,
    bool DualPartyNotification,
    int RetentionDays);

public sealed record SpeakerSegmentDto(
    string Speaker,
    int StartOffsetSeconds,
    int EndOffsetSeconds,
    string Text);

public sealed record CallRecordingDto(
    Guid CallId,
    string CallSid,
    string? RecordingUrl,
    int? RecordingDurationSeconds,
    bool IsRecordingDeleted,
    DateTime StartedAtUtc,
    string Direction,
    string Status,
    string? ToNumber,
    string? FromNumber,
    string ConsentMode,
    bool DualPartyNotification);

public sealed record TranscribeCallRequest(
    string? Language = "en-US",
    bool IncludeSentiment = true,
    bool IncludeSummary = true,
    bool IncludeActionItems = true);

public sealed record TranscriptionResultDto(
    Guid CallId,
    string CallSid,
    string Status,
    string? FullText,
    string? Summary,
    string? Sentiment,
    IReadOnlyList<string> ActionItems,
    IReadOnlyList<SpeakerSegmentDto> Segments,
    DateTime? CompletedAtUtc);

public sealed record TranscriptionWebhookRequest(
    string CallSid,
    string? Status,
    string? FullText,
    string? Summary,
    string? Sentiment,
    string[]? ActionItems);

public sealed record GdprDeleteRecordingResult(
    Guid CallId,
    bool Success,
    string Message);
