using MindLift.Domain.Common;
using MindLift.Shared.Enums;

namespace MindLift.Domain.Entities;

/// <summary>A role. Users get permissions from their role (plan 4.4.2). IsAdmin bypasses every check.</summary>
[Audited]
public sealed class Role : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool IsAdmin { get; set; }
    public DashboardView DashboardView { get; set; } = DashboardView.Staff;
}

/// <summary>What a role may do in one module (plan 4.4.2).</summary>
[Audited]
public sealed class RolePermission : TenantEntity
{
    public Guid RoleId { get; set; }
    public AppModule Module { get; set; }
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanExport { get; set; }
    public bool CanImport { get; set; }
    public DataScope Scope { get; set; } = DataScope.Own;
}

/// <summary>Whether a module is enabled for the tenant, and its display labels (plan 5.1).</summary>
[Audited]
public sealed class ModuleSetting : TenantEntity
{
    public AppModule Module { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string SingularLabel { get; set; } = string.Empty;
    public string PluralLabel { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>A team inside the tenant. Team members form the Team data scope.</summary>
[Audited]
public sealed class Team : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public Guid? LeaderId { get; set; }
}

/// <summary>A branch or office.</summary>
[Audited]
public sealed class Branch : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? TimeZoneId { get; set; }
}

/// <summary>An invitation to join a tenant. Only the hash of the token is stored (plan 5.2).</summary>
[Audited]
public sealed class Invitation : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public Guid? TeamId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid InvitedById { get; set; }
}

/// <summary>
/// A refresh token (plan 4.3). Stored hashed. Not tenant-filtered because it is looked up before the tenant is known.
/// Rows are really deleted (hard delete) by the cleanup job.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>All tokens issued by one login share a family. Reuse of a revoked token revokes the family.</summary>
    public Guid FamilyId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByHash { get; set; }
}

/// <summary>A picklist option such as a lead status or a lead source (plan 5.3).</summary>
[Audited]
public sealed class PicklistValue : TenantEntity
{
    public string PicklistKey { get; set; } = string.Empty;

    /// <summary>Stable code, never changes after creation.</summary>
    public string Value { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string? LabelAr { get; set; }

    /// <summary>Semantic meaning used by code (for example New, Converted). Code never checks the label.</summary>
    public string? Category { get; set; }

    public string? Color { get; set; }
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }

    /// <summary>System, Template or User. Template rows are removed and recreated when an industry template is applied again.</summary>
    public string Origin { get; set; } = "User";
}

