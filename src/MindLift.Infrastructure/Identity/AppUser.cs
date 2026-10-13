using Microsoft.AspNetCore.Identity;

namespace MindLift.Infrastructure.Identity;

/// <summary>
/// A staff user. Emails are unique across all tenants (one user belongs to one tenant, plan 4.1.6).
/// Permissions come from the user's role (added in M2), not from Identity roles.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsTenantOwner { get; set; }
    public bool IsPlatformAdmin { get; set; }
    public string Language { get; set; } = "en";
    public string? JobTitle { get; set; }
    public Guid? RoleId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? BranchId { get; set; }

    /// <summary>Direct manager. Drives the Team data scope together with TeamId.</summary>
    public Guid? ManagerId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
