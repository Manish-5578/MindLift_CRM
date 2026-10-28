using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 5 / #31: BCC Dropbox (bcc-dropbox).
/// Stores a user's dedicated inbound BCC dropbox address and token.
/// </summary>
[Audited]
public sealed class BccDropboxAddress : TenantEntity
{
    public Guid UserId { get; set; }
    public string DropboxToken { get; set; } = string.Empty;
    public string DropboxEmail { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int TotalEmailsReceived { get; set; }
    public DateTime? LastReceivedAtUtc { get; set; }
}

/// <summary>
/// Phase 5 / #31: Tenant-wide BCC dropbox configuration.
/// </summary>
[Audited]
public sealed class BccDropboxSettings : TenantEntity
{
    public string DropboxDomain { get; set; } = "bcc.mindlift.crm";
    public bool AutoCreateLead { get; set; }
    public bool AutoCreateContact { get; set; }
    public bool MatchDeals { get; set; } = true;
    public string DefaultPrivacy { get; set; } = "Public"; // "Public", "Private"
    public string[] ExcludedDomains { get; set; } = [];
    public bool LogNonMatched { get; set; } = true;
}

/// <summary>
/// Phase 5 / #31: Log entry of each inbound email received through the BCC dropbox.
/// </summary>
[Audited]
public sealed class BccDropboxLog : TenantEntity
{
    public Guid BccDropboxAddressId { get; set; }
    public Guid UserId { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string[] RecipientAddresses { get; set; } = [];
    public string Subject { get; set; } = string.Empty;
    public Guid? MatchedLeadId { get; set; }
    public Guid? MatchedContactId { get; set; }
    public Guid? MatchedAccountId { get; set; }
    public Guid? MatchedDealId { get; set; }
    public string ActionTaken { get; set; } = "MatchedExisting"; // "MatchedExisting", "CreatedLead", "CreatedContact", "LoggedUnmatched", "SkippedExcludedDomain"
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedMessageId { get; set; }
}
