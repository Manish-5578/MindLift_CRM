using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// A secret a company gives us to talk to another service (an API key, a token, a signing secret).
/// The secret is stored encrypted and is never sent back to a browser; only the label and the non-secret settings are shown.
/// </summary>
[Audited]
public sealed class IntegrationCredential : TenantEntity
{
    /// <summary>Which service this is for, for example <c>twilio</c> or <c>stripe</c>.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>A name the company chooses so it can keep more than one (for example "Sales line").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The secret, encrypted by <c>ISecretProtector</c>. Never read this directly; use the vault.</summary>
    public string EncryptedSecret { get; set; } = string.Empty;

    /// <summary>Settings that are not secret (account id, region), as JSON text.</summary>
    public string? SettingsJson { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RotatedAt { get; set; }
}
