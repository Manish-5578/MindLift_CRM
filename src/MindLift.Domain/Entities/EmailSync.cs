using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 5 / #29: Two-way email sync (email-sync).
/// Stores a user's connected email mailbox credentials and synchronization configuration.
/// </summary>
[Audited]
public sealed class EmailSyncAccount : TenantEntity
{
    public Guid UserId { get; set; }
    public string EmailAddress { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Provider { get; set; } = "IMAP"; // "IMAP", "Google", "Microsoft365", "Exchange"
    
    public string? ImapHost { get; set; }
    public int? ImapPort { get; set; } = 993;
    public bool ImapUseSsl { get; set; } = true;
    public string? ImapUsername { get; set; }
    public string? EncryptedPassword { get; set; }

    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string? SmtpUsername { get; set; }

    public string? EncryptedOAuthToken { get; set; }
    public string? EncryptedRefreshToken { get; set; }

    public string SyncDirection { get; set; } = "TwoWay"; // "TwoWay", "InboundOnly", "OutboundOnly"
    public string EmailVisibility { get; set; } = "Public"; // "Public", "Private"
    public bool AutoCreateLeads { get; set; }
    public string[] ExcludedDomains { get; set; } = [];
    public string[] SyncFolders { get; set; } = ["INBOX", "Sent"];

    public bool IsActive { get; set; } = true;
    public DateTime? LastSyncedAt { get; set; }
    public string SyncStatus { get; set; } = "Idle"; // "Idle", "Syncing", "Success", "Error"
    public string? LastErrorMessage { get; set; }
    public int TotalSyncedCount { get; set; }
}

/// <summary>
/// Phase 5 / #29: Synchronized email message record linked to CRM leads, contacts, accounts, and deals.
/// </summary>
[Audited]
public sealed class EmailSyncMessage : TenantEntity
{
    public Guid EmailSyncAccountId { get; set; }
    public string ExternalMessageId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }
    public string[] ToAddresses { get; set; } = [];
    public string[] CcAddresses { get; set; } = [];
    public string[] BccAddresses { get; set; } = [];
    public DateTime DateUtc { get; set; }
    public string? BodyPlain { get; set; }
    public string? BodyHtml { get; set; }
    public string? Snippet { get; set; }
    public string Direction { get; set; } = "Inbound"; // "Inbound", "Outbound"
    public bool IsRead { get; set; }
    public bool HasAttachments { get; set; }
    public string AttachmentsJson { get; set; } = "[]";

    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Phase 5 / #29: Audit log of mailbox synchronization runs.
/// </summary>
[Audited]
public sealed class EmailSyncLog : TenantEntity
{
    public Guid EmailSyncAccountId { get; set; }
    public string Status { get; set; } = "Success"; // "Success", "Partial", "Failed"
    public int MessagesFetched { get; set; }
    public int MessagesCreated { get; set; }
    public int MessagesLinked { get; set; }
    public int MessagesSkipped { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}
