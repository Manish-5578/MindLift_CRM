using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// An address a company must not email again: the person unsubscribed, or their mailbox bounced or complained. Checked before any
/// automated mail goes out, so one company's mail cannot keep hitting people who said no (which is what gets a sending server blacklisted).
/// </summary>
public sealed class EmailSuppression : TenantEntity
{
    /// <summary>Lower case.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>"unsubscribed", "bounced", "complained" or "manual".</summary>
    public string Reason { get; set; } = "unsubscribed";
}

/// <summary>
/// A domain a company sends mail from (for example "acme.com"). Proving the company owns it, and that its SPF and DMARC records are in place,
/// is what lets mail be sent in the company's own name instead of the platform's.
/// </summary>
public sealed class SendingDomain : TenantEntity
{
    public string Domain { get; set; } = string.Empty;

    /// <summary>The value the company puts in a TXT record to prove the domain is theirs.</summary>
    public string VerificationToken { get; set; } = string.Empty;

    public bool OwnershipVerified { get; set; }
    public bool SpfOk { get; set; }
    public bool DmarcOk { get; set; }
    public DateTime? LastCheckedAt { get; set; }

    /// <summary>What the last check found, in words, for the settings page.</summary>
    public string? LastCheckMessage { get; set; }
}

/// <summary>A download of all of a company's data, prepared in the background and kept for a week.</summary>
public sealed class DataExport
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid? RequestedById { get; set; }

    /// <summary>"running", "ready" or "failed".</summary>
    public string Status { get; set; } = "running";

    public string? StorageKey { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
