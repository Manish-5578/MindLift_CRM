using MindLift.Domain.Common;
using MindLift.Shared.Contracts;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 4 / #27: GDPR and unsubscribe center (unsubscribe-center).
/// Formal audit log of consent decisions complying with GDPR Article 7.
/// </summary>
[Audited]
public sealed class GdprConsentRecord : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public CommunicationChannel Channel { get; set; } = CommunicationChannel.Email;

    /// <summary>
    /// Topic or communication category: "All", "Marketing", "Sales", "ProductUpdates", "Surveys"
    /// </summary>
    public string Topic { get; set; } = "Marketing";

    public ConsentStatus Status { get; set; } = ConsentStatus.OptedIn;
    public GdprLawfulBasis LawfulBasis { get; set; } = GdprLawfulBasis.Consent;

    /// <summary>
    /// Source of consent entry: "UnsubscribeCenter", "OneClickRfc8058", "WebForm", "Api", "ManualAdmin"
    /// </summary>
    public string Source { get; set; } = "UnsubscribeCenter";

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Notes { get; set; }
    public DateTime EffectiveAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cryptographically secure, expiring token for self-service subscription preference management.
/// </summary>
[Audited]
public sealed class SubscriptionPreferenceToken : TenantEntity
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(90);
    public DateTime? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }
}

/// <summary>
/// Audit record for GDPR Article 17 "Right to be Forgotten" data erasure requests.
/// </summary>
[Audited]
public sealed class GdprErasureRecord : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string Status { get; set; } = "Completed"; // "Completed", "Pending", "Rejected"
    public int RecordsErased { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}
