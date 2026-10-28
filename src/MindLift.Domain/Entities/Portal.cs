using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

[Audited]
public sealed class PortalSettings : TenantEntity
{
    public bool IsEnabled { get; set; } = true;
    public string? LogoUrl { get; set; }
    public string? WelcomeMessage { get; set; } = "Welcome to your customer portal. View your shared documents and submit requested files securely.";
    public string? WelcomeMessageAr { get; set; }
    public bool ShowMetrics { get; set; } = true;
    public bool AllowGeneralUploads { get; set; } = false;
    public bool NotifyCustomerOnExpiry { get; set; } = true;
    public string? SupportEmail { get; set; }
    public string? SupportPhone { get; set; }
}

[Audited]
public sealed class PortalUser : TenantEntity
{
    public Guid ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? InvitedAt { get; set; }
    public Guid? InvitedById { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string Language { get; set; } = "en";
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>
    /// Carried in the portal access token as <c>ver</c>. It changes only when every session must end (sign-out, password change,
    /// deactivation). <see cref="TenantEntity.Version"/> cannot be used for this: it changes on every ordinary edit of the row.
    /// </summary>
    public int SessionVersion { get; set; } = 1;
}

public sealed class PortalToken : TenantEntity
{
    public Guid PortalUserId { get; set; }
    public string Purpose { get; set; } = "MagicLink"; // Invite, MagicLink, ResetPassword
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}

[Audited]
public sealed class DocumentRequest : TenantEntity
{
    public Guid ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Status { get; set; } = "Open"; // Open, Submitted, Completed, Cancelled
}
