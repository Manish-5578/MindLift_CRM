using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 5 / #32: Click-to-call & Telephony settings per tenant.
/// </summary>
[Audited]
public sealed class TelephonySettings : TenantEntity
{
    public string Provider { get; set; } = "MockTelephony"; // "MockTelephony", "Twilio", "Vonage", "Plivo"
    public string? AccountSid { get; set; }
    public string? EncryptedAuthToken { get; set; }
    public string CallerId { get; set; } = "+18005550199";
    public bool IsActive { get; set; } = true;
    public bool AutoLogCalls { get; set; } = true;
    public bool RecordCallsDefault { get; set; }
    public bool WebRtcEnabled { get; set; } = true;
    public string? TwilioAppSid { get; set; }
    public string ConsentMode { get; set; } = "Prompt"; // "Always", "Prompt", "None"
    public bool DualPartyNotification { get; set; } = true;
    public int RetentionDays { get; set; } = 90;
}

/// <summary>
/// Phase 5 / #32 & #33: Call session tracking and call recording/transcription.
/// </summary>
[Audited]
public sealed class CallSession : TenantEntity
{
    public Guid UserId { get; set; }
    public string CallSid { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string ToNumber { get; set; } = string.Empty;
    public string Direction { get; set; } = "Outbound"; // "Outbound", "Inbound"
    public string Status { get; set; } = "Initiated"; // "Initiated", "Ringing", "InProgress", "Completed", "Busy", "Failed", "NoAnswer", "Canceled"
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AnsweredAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public int DurationSeconds { get; set; }

    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }

    public string? Notes { get; set; }
    public string? Disposition { get; set; } // "Connected", "Left Voicemail", "Wrong Number", "Busy", "Not Interested"

    // Phase 5 / #33 Call recording & transcription
    public string? RecordingUrl { get; set; }
    public int? RecordingDurationSeconds { get; set; }
    public string? TranscriptionText { get; set; }
    public string? TranscriptionStatus { get; set; } = "None"; // "None", "Pending", "Processing", "Completed", "Failed"
    public string? TranscriptionSummary { get; set; }
    public string? TranscriptionSentiment { get; set; } // "Positive", "Neutral", "Negative"
    public string[] TranscriptionActionItems { get; set; } = [];
    public bool IsRecordingDeleted { get; set; }
}
