using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Auth;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Administration;

/// <summary>User management (plan 7.1). Every query runs inside the caller's tenant.</summary>
public sealed class UserAdministrationService(
    AppDbContext db,
    UserManager<AppUser> users,
    ITenantContext tenant,
    IClock clock,
    IPlanLimitService planLimits,
    RefreshTokenService refreshTokens)
{
    private const int MaxPageSize = 200;

    public async Task<PagedResult<UserListItem>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.NormalizedEmail!.Contains(term.ToUpperInvariant()) ||
                u.FirstName.ToLower().Contains(term) ||
                u.LastName.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var roleNames = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var items = rows.Select(u => new UserListItem(
            u.Id,
            u.Email ?? string.Empty,
            $"{u.FirstName} {u.LastName}".Trim(),
            u.IsActive,
            u.IsTenantOwner,
            u.RoleId,
            u.RoleId is { } roleId && roleNames.TryGetValue(roleId, out var name) ? name : null,
            u.TeamId,
            u.LastLoginAt)).ToList();

        return new PagedResult<UserListItem>(items, total, page, pageSize);
    }

    public async Task<UserDetail> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Map(await LoadAsync(id, cancellationToken));

    private async Task<bool> IsCallerAdminAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsPlatformAdmin) return true;
        if (tenant.UserId is not { } callerId) return false;
        var caller = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken);
        if (caller == null) return false;
        if (caller.IsTenantOwner) return true;
        if (caller.RoleId is null) return false;
        return await db.Roles.AsNoTracking().AnyAsync(r => r.Id == caller.RoleId && r.IsAdmin, cancellationToken);
    }

    public async Task<UserDetail> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await LoadAsync(id, cancellationToken);
        var isCallerAdmin = await IsCallerAdminAsync(cancellationToken);

        var currentRole = user.RoleId is null ? null : await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        if ((user.IsTenantOwner || currentRole?.IsAdmin == true) && !isCallerAdmin)
        {
            throw new ForbiddenException("Only administrators can modify administrator accounts.", "cannot_modify_admin");
        }

        if (request.RoleId is { } roleId && roleId != user.RoleId)
        {
            var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
                ?? throw new NotFoundException("Role", roleId);

            if (role.IsAdmin && !isCallerAdmin)
            {
                throw new ForbiddenException("Only administrators can assign the Admin role.", "admin_role_required");
            }

            if (currentRole?.IsAdmin == true && !role.IsAdmin)
            {
                await EnsureAnotherAdminAsync(user.Id, cancellationToken);
            }

            if (user.IsTenantOwner && !role.IsAdmin)
            {
                throw new ConflictException("owner_must_be_admin", "The workspace owner must keep the Admin role.");
            }
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = request.Phone;
        user.JobTitle = request.JobTitle;
        user.Language = request.Language == "ar" ? "ar" : "en";
        user.RoleId = request.RoleId ?? user.RoleId;
        user.TeamId = request.TeamId;
        user.BranchId = request.BranchId;
        user.ManagerId = request.ManagerId;

        if (request.ManagerId == user.Id)
        {
            throw new ConflictException("invalid_manager", "A user cannot be their own manager.");
        }

        await users.UpdateAsync(user);
        return Map(user);
    }

    /// <summary>Deactivates a user. Their refresh tokens are revoked immediately. Open records are reassigned in M6+.</summary>
    public async Task DeactivateAsync(Guid id, Guid? reassignToUserId, CancellationToken cancellationToken = default)
    {
        var user = await LoadAsync(id, cancellationToken);

        if (user.IsTenantOwner)
        {
            throw new ConflictException("owner_cannot_be_deactivated", "The workspace owner cannot be deactivated.");
        }

        if (user.Id == tenant.UserId)
        {
            throw new ConflictException("cannot_deactivate_self", "You cannot deactivate your own account.");
        }

        var isCallerAdmin = await IsCallerAdminAsync(cancellationToken);
        var targetRole = user.RoleId is null ? null : await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        if (targetRole?.IsAdmin == true && !isCallerAdmin)
        {
            throw new ForbiddenException("Only administrators can deactivate administrator accounts.", "admin_required");
        }

        if (reassignToUserId is { } target && !await db.Users.AnyAsync(u => u.Id == target && u.IsActive, cancellationToken))
        {
            throw new NotFoundException("User", target);
        }

        user.IsActive = false;
        await users.UpdateAsync(user);
        await refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);
    }

    public async Task ReactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await LoadAsync(id, cancellationToken);
        if (user.IsActive)
        {
            return;
        }

        await planLimits.EnsureSeatAvailableAsync(cancellationToken);
        user.IsActive = true;
        await users.UpdateAsync(user);
    }

    public async Task<UserDetail> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await LoadAsync(userId, cancellationToken);
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = request.Phone;
        user.JobTitle = request.JobTitle;
        user.Language = request.Language == "ar" ? "ar" : "en";
        await users.UpdateAsync(user);
        return Map(user);
    }

    private async Task<AppUser> LoadAsync(Guid id, CancellationToken cancellationToken)
        => await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("User", id);

    private async Task EnsureAnotherAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var adminRoleIds = await db.Roles.AsNoTracking().Where(r => r.IsAdmin).Select(r => r.Id).ToListAsync(cancellationToken);
        var otherAdmins = await db.Users.AsNoTracking()
            .CountAsync(u => u.IsActive && u.Id != userId && u.RoleId != null && adminRoleIds.Contains(u.RoleId.Value), cancellationToken);

        if (otherAdmins == 0)
        {
            throw new ConflictException("last_admin", "The last Admin cannot be demoted. Make someone else an Admin first.");
        }
    }

    private static UserDetail Map(AppUser u) => new(
        u.Id,
        u.Email ?? string.Empty,
        u.FirstName,
        u.LastName,
        u.PhoneNumber,
        u.JobTitle,
        u.Language,
        u.IsActive,
        u.IsTenantOwner,
        u.RoleId,
        u.TeamId,
        u.BranchId,
        u.ManagerId,
        u.LastLoginAt,
        u.CreatedAt);
}
