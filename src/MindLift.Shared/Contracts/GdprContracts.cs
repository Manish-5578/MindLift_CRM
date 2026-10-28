namespace MindLift.Shared.Contracts;

public enum ConsentStatus
{
    OptedIn = 1,
    OptedOut = 2,
    Pending = 3
}

public enum GdprLawfulBasis
{
    Consent = 1,
    LegitimateInterest = 2,
    Contract = 3,
    LegalObligation = 4
}

public sealed class GdprConsentRecordDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public CommunicationChannel Channel { get; set; }
    public string Topic { get; set; } = "Marketing";
    public ConsentStatus Status { get; set; }
    public GdprLawfulBasis LawfulBasis { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Notes { get; set; }
    public DateTime EffectiveAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SaveConsentRecordRequest
{
    public string Email { get; set; } = string.Empty;
    public CommunicationChannel Channel { get; set; } = CommunicationChannel.Email;
    public string Topic { get; set; } = "Marketing";
    public ConsentStatus Status { get; set; } = ConsentStatus.OptedIn;
    public GdprLawfulBasis LawfulBasis { get; set; } = GdprLawfulBasis.Consent;
    public string Source { get; set; } = "ManualAdmin";
    public string? Notes { get; set; }
}

public sealed class TopicPreferenceItem
{
    public string Topic { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSubscribed { get; set; }
}

public sealed class PreferenceCenterDetailsDto
{
    public string Email { get; set; } = string.Empty;
    public bool GlobalUnsubscribe { get; set; }
    public List<TopicPreferenceItem> Topics { get; set; } = [];
    public DateTime? LastUpdatedAt { get; set; }
}

public sealed class UpdatePreferencesRequest
{
    public string? Token { get; set; }
    public string? Email { get; set; }
    public bool GlobalUnsubscribe { get; set; }
    public Dictionary<string, bool> TopicPreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Reason { get; set; }
}

public sealed class GeneratePreferenceTokenRequest
{
    public string Email { get; set; } = string.Empty;
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
}

public sealed class PreferenceTokenDto
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PreferenceCenterUrl { get; set; } = string.Empty;
    public string OneClickUnsubscribeUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public sealed class ConsentCheckResult
{
    public string Email { get; set; } = string.Empty;
    public CommunicationChannel Channel { get; set; }
    public string Topic { get; set; } = "Marketing";
    public bool IsAllowed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ConsentStatus? CurrentStatus { get; set; }
}

public sealed class GdprSubjectRecord
{
    public Guid Id { get; set; }
    public string RecordType { get; set; } = "Lead";
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? CompanyOrAccount { get; set; }
    public string? Title { get; set; }
    public string? Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class GdprSubjectDataExport
{
    public string SubjectEmail { get; set; } = string.Empty;
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public List<GdprSubjectRecord> Records { get; set; } = [];
    public List<GdprConsentRecordDto> ConsentHistory { get; set; } = [];
}

public sealed class GdprErasureRequest
{
    public string Email { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = "GDPR Right to Erasure Request";
}

public sealed class GdprErasureResult
{
    public string Email { get; set; } = string.Empty;
    public int RecordsErased { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
}
