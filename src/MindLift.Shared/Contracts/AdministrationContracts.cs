using MindLift.Shared.Enums;

namespace MindLift.Shared.Contracts;

/// <summary>A page of results. Pages are 1-based. PageSize is capped at 200 (plan 4.6.5).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record UserListItem(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive,
    bool IsTenantOwner,
    Guid? RoleId,
    string? RoleName,
    Guid? TeamId,
    DateTime? LastLoginAtUtc);

public sealed record UserDetail(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    string? JobTitle,
    string Language,
    bool IsActive,
    bool IsTenantOwner,
    Guid? RoleId,
    Guid? TeamId,
    Guid? BranchId,
    Guid? ManagerId,
    DateTime? LastLoginAtUtc,
    DateTime CreatedAtUtc);

public sealed record UpdateUserRequest(
    string FirstName,
    string LastName,
    string? Phone,
    string? JobTitle,
    string Language,
    Guid? RoleId,
    Guid? TeamId,
    Guid? BranchId,
    Guid? ManagerId);

public sealed record UpdateProfileRequest(string FirstName, string LastName, string? Phone, string? JobTitle, string Language);

public sealed record DeactivateUserRequest(Guid? ReassignToUserId);

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, bool IsAdmin, DashboardView DashboardView, int UserCount);

public sealed record SaveRoleRequest(string Name, string? Description, DashboardView DashboardView);

public sealed record RolePermissionDto(
    AppModule Module,
    bool CanView,
    bool CanCreate,
    bool CanEdit,
    bool CanDelete,
    bool CanExport,
    bool CanImport,
    DataScope Scope);

public sealed record SaveRolePermissionsRequest(IReadOnlyList<RolePermissionDto> Permissions);

public sealed record TeamDto(Guid Id, string Name, Guid? BranchId, Guid? LeaderId, int MemberCount);

public sealed record SaveTeamRequest(string Name, Guid? BranchId, Guid? LeaderId);

public sealed record BranchDto(Guid Id, string Name, string? Address, string? TimeZoneId);

public sealed record SaveBranchRequest(string Name, string? Address, string? TimeZoneId);

public sealed record InvitationDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    Guid RoleId,
    Guid? TeamId,
    string Status,
    DateTime ExpiresAtUtc,
    DateTime CreatedAtUtc);

public sealed record CreateInvitationRequest(string Email, string FirstName, string LastName, Guid RoleId, Guid? TeamId);

public sealed record AcceptInvitationRequest(string Token, string Password);

public sealed record InvitationPreview(string Email, string FirstName, string LastName, string CompanyName, DateTime ExpiresAtUtc);

public sealed record AuditLogDto(
    Guid Id,
    Guid? UserId,
    string EntityType,
    Guid EntityId,
    string Action,
    string? Changes,
    DateTime OccurredAtUtc);

public sealed record PlatformTenantDto(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    string PlanKey,
    int UserCount,
    DateTime? TrialEndsAtUtc,
    DateTime CreatedAtUtc);

public sealed record ChangeTenantStatusRequest(string Status);

public sealed record ChangeTenantPlanRequest(string PlanKey);
