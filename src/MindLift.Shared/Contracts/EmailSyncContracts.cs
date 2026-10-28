using System.ComponentModel.DataAnnotations;

namespace MindLift.Shared.Contracts;

public sealed class EmailSyncAccountDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string EmailAddress { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Provider { get; set; } = "IMAP";
    public string? ImapHost { get; set; }
    public int? ImapPort { get; set; }
    public bool ImapUseSsl { get; set; }
    public string? ImapUsername { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public bool SmtpUseSsl { get; set; }
    public string? SmtpUsername { get; set; }
    public string SyncDirection { get; set; } = "TwoWay";
    public string EmailVisibility { get; set; } = "Public";
    public bool AutoCreateLeads { get; set; }
    public string[] ExcludedDomains { get; set; } = [];
    public string[] SyncFolders { get; set; } = [];
    public bool IsActive { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string SyncStatus { get; set; } = "Idle";
    public string? LastErrorMessage { get; set; }
    public int TotalSyncedCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CreateEmailSyncAccountRequest
{
    [Required, EmailAddress]
    public string EmailAddress { get; set; } = string.Empty;
    [Required, MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;
    public string Provider { get; set; } = "IMAP";

    public string? ImapHost { get; set; }
    public int? ImapPort { get; set; } = 993;
    public bool ImapUseSsl { get; set; } = true;
    public string? ImapUsername { get; set; }
    public string? Password { get; set; }

    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string? SmtpUsername { get; set; }

    public string? OAuthToken { get; set; }
    public string? RefreshToken { get; set; }

    public string SyncDirection { get; set; } = "TwoWay";
    public string EmailVisibility { get; set; } = "Public";
    public bool AutoCreateLeads { get; set; }
    public string[]? ExcludedDomains { get; set; }
    public string[]? SyncFolders { get; set; }
}

public sealed class UpdateEmailSyncAccountRequest
{
    [MaxLength(100)]
    public string? DisplayName { get; set; }
    public string? SyncDirection { get; set; }
    public string? EmailVisibility { get; set; }
    public bool? AutoCreateLeads { get; set; }
    public string[]? ExcludedDomains { get; set; }
    public string[]? SyncFolders { get; set; }
    public bool? IsActive { get; set; }
    public string? Password { get; set; }
}

public sealed class EmailSyncMessageDto
{
    public Guid Id { get; set; }
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
    public string Direction { get; set; } = "Inbound";
    public bool IsRead { get; set; }
    public bool HasAttachments { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
    public DateTime SyncedAt { get; set; }
}

public sealed class EmailSyncLogDto
{
    public Guid Id { get; set; }
    public Guid EmailSyncAccountId { get; set; }
    public string Status { get; set; } = "Success";
    public int MessagesFetched { get; set; }
    public int MessagesCreated { get; set; }
    public int MessagesLinked { get; set; }
    public int MessagesSkipped { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class EmailSyncResultDto
{
    public Guid AccountId { get; set; }
    public string Status { get; set; } = "Success";
    public int Fetched { get; set; }
    public int Created { get; set; }
    public int Linked { get; set; }
    public int Skipped { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    public string? Message { get; set; }
}

public sealed class TestMailboxConnectionRequest
{
    public string Provider { get; set; } = "IMAP";
    public string? Host { get; set; }
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class TestMailboxConnectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int LatencyMs { get; set; }
}

public sealed class SendSyncEmailRequest
{
    [Required]
    public string To { get; set; } = string.Empty;
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    [Required]
    public string Subject { get; set; } = string.Empty;
    [Required]
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
    public string? InReplyToMessageId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
}

public sealed class SimulateInboundEmailRequest
{
    [Required]
    public string From { get; set; } = string.Empty;
    public string? FromName { get; set; }
    [Required]
    public string To { get; set; } = string.Empty;
    [Required]
    public string Subject { get; set; } = string.Empty;
    [Required]
    public string Body { get; set; } = string.Empty;
    public string? MessageId { get; set; }
    public string? InReplyTo { get; set; }
    public DateTime? DateUtc { get; set; }
}

public sealed class EmailSyncStatsDto
{
    public int TotalAccounts { get; set; }
    public int ActiveAccounts { get; set; }
    public int TotalMessagesSynced { get; set; }
    public int InboundCount { get; set; }
    public int OutboundCount { get; set; }
    public int LinkedLeadsCount { get; set; }
    public int LinkedContactsCount { get; set; }
    public DateTime? LastSyncTimeUtc { get; set; }
}
