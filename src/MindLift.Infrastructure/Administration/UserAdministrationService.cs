using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    RefreshTokenService refreshTokens,
    IDataScopeService scope,
    IEmailSender email,
    IOptions<AppOptions> app)
{
    private const int MaxManagerChainDepth = 50;

    private const int MaxPageSize = 200;

    public async Task<PagedResult<UserListItem>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = db.Users.AsNoTracking().AsQueryable();
        var visible = await VisibleUserIdsAsync(cancellationToken);
        if (visible is not null)
        {
            query = query.Where(u => visible.Contains(u.Id));
        }
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
            u.LastLoginAt,
            u.MustChangePassword)).ToList();

        return new PagedResult<UserListItem>(items, total, page, pageSize);
    }

    public async Task<UserDetail> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInScopeAsync(id, cancellationToken);
        return Map(await LoadAsync(id, cancellationToken));
    }

    /// <summary>Null = every user. Admins see everyone; others are limited to their Users-module data scope.</summary>
    private async Task<IReadOnlyList<Guid>?> VisibleUserIdsAsync(CancellationToken cancellationToken)
        => await IsCallerAdminAsync(cancellationToken) ? null : await scope.GetScopeUserIdsAsync(MindLift.Shared.Enums.AppModule.Users, cancellationToken);

    private async Task EnsureInScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var visible = await VisibleUserIdsAsync(cancellationToken);
        if (visible is not null && !visible.Contains(userId))
        {
            throw new NotFoundException("User", userId);
        }
    }

    private async Task EnsureValidManagerAsync(Guid userId, Guid? managerId, CancellationToken cancellationToken)
    {
        if (managerId is not { } current)
        {
            return;
        }

        if (current == userId)
        {
            throw new ConflictException("invalid_manager", "A user cannot be their own manager.");
        }

        if (!await db.Users.AnyAsync(u => u.Id == current && u.IsActive, cancellationToken))
        {
            throw new NotFoundException("User", current);
        }

        // Walk up the chain: the new manager must not (indirectly) report to this user.
        Guid? cursor = current;
        for (var depth = 0; cursor is { } step && depth < MaxManagerChainDepth; depth++)
        {
            cursor = await db.Users.AsNoTracking().Where(u => u.Id == step).Select(u => u.ManagerId).FirstOrDefaultAsync(cancellationToken);
            if (cursor == userId)
            {
                throw new ConflictException("invalid_manager", "This would create a reporting loop.");
            }
        }
    }

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
        await EnsureInScopeAsync(id, cancellationToken);
        var user = await LoadAsync(id, cancellationToken);
        var isCallerAdmin = await IsCallerAdminAsync(cancellationToken);

        // Team, branch and manager drive Team-scoped data access, so only administrators may change them.
        var hierarchyChanged = request.TeamId != user.TeamId || request.BranchId != user.BranchId || request.ManagerId != user.ManagerId;
        if (hierarchyChanged && !isCallerAdmin)
        {
            throw new ForbiddenException("Only administrators can change a user's team, branch or manager.", "admin_required");
        }

        if (request.ManagerId != user.ManagerId)
        {
            await EnsureValidManagerAsync(user.Id, request.ManagerId, cancellationToken);
        }

        if (request.TeamId is { } newTeam && newTeam != user.TeamId && !await db.Teams.AnyAsync(t => t.Id == newTeam, cancellationToken))
        {
            throw new NotFoundException("Team", newTeam);
        }

        if (request.BranchId is { } newBranch && newBranch != user.BranchId && !await db.Branches.AnyAsync(b => b.Id == newBranch, cancellationToken))
        {
            throw new NotFoundException("Branch", newBranch);
        }

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
        await EnsureInScopeAsync(id, cancellationToken);
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
        await EnsureInScopeAsync(id, cancellationToken);
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

    public async Task<UserDetail> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = users.NormalizeEmail(request.Email);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            throw new ConflictException("email_taken", "An account with this email already exists.");
        }

        await planLimits.EnsureSeatAvailableAsync(cancellationToken);
        var role = await EnsureRoleAndTeamAsync(request.RoleId, request.TeamId, cancellationToken);
        await EnsureCallerCanGrantRoleAsync(role, cancellationToken);

        if (request.BranchId is { } branchId && !await db.Branches.AnyAsync(b => b.Id == branchId, cancellationToken))
        {
            throw new NotFoundException("Branch", branchId);
        }

        var tenantId = tenant.TenantId ?? throw new UnauthorizedException("missing_tenant", "No workspace tenant found.");
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = request.Phone,
            JobTitle = request.JobTitle,
            RoleId = role.Id,
            TeamId = request.TeamId,
            BranchId = request.BranchId,
            IsActive = true,
            EmailConfirmed = true,
            LockoutEnabled = true,
            MustChangePassword = true,
            CreatedAt = clock.UtcNow,
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var passwordErrors = result.Errors
                .Where(e => e.Code.StartsWith("Password", StringComparison.Ordinal))
                .Select(e => e.Description)
                .ToArray();

            if (passwordErrors.Length > 0)
            {
                throw new MindLift.Application.Common.Exceptions.ValidationException(new Dictionary<string, string[]>
                {
                    ["password"] = passwordErrors,
                });
            }

            throw new ConflictException("user_create_failed", string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        var appUrl = app.Value.BaseUrl.TrimEnd('/');
        var companyName = (await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken))?.Name ?? "MindLift";
        var emailBody = $"Hello {user.FirstName},\n\nYou have been added to {companyName} on MindLift.\n\nLogin Email: {user.Email}\nTemporary Password: {request.Password}\n\nPlease sign in at {appUrl}/login and you will be prompted to reset your password.\n\nRegards,\nMindLift Team";
        try
        {
            await email.SendAsync(user.Email, $"Welcome to {companyName} on MindLift", emailBody, cancellationToken);
        }
        catch
        {
            // Silently ignore mail failures so account creation succeeds in all environments
        }

        return Map(user);
    }

    private async Task<Role> EnsureRoleAndTeamAsync(Guid roleId, Guid? teamId, CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw new NotFoundException("Role", roleId);

        if (teamId is { } team && !await db.Teams.AnyAsync(t => t.Id == team, cancellationToken))
        {
            throw new NotFoundException("Team", team);
        }

        return role;
    }

    private async Task EnsureCallerCanGrantRoleAsync(Role targetRole, CancellationToken cancellationToken)
    {
        if (tenant.IsPlatformAdmin) return;
        if (tenant.UserId is not { } callerId)
            throw new ForbiddenException("Caller not found.", "forbidden");

        var caller = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken);
        if (caller == null)
            throw new ForbiddenException("Caller not found.", "forbidden");

        if (caller.IsTenantOwner) return;

        if (caller.RoleId is null)
            throw new ForbiddenException("You do not have a role and cannot grant roles.", "grant_forbidden");

        var callerRole = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == caller.RoleId, cancellationToken);
        if (callerRole == null)
            throw new ForbiddenException("Caller role not found.", "forbidden");

        if (callerRole.IsAdmin) return;

        if (targetRole.IsAdmin)
        {
            throw new ForbiddenException("Only administrators can grant or invite the Admin role.", "admin_role_required");
        }

        var callerPermissions = await db.RolePermissions.AsNoTracking()
            .Where(p => p.RoleId == callerRole.Id)
            .ToDictionaryAsync(p => p.Module, cancellationToken);

        var targetPermissions = await db.RolePermissions.AsNoTracking()
            .Where(p => p.RoleId == targetRole.Id)
            .ToListAsync(cancellationToken);

        foreach (var targetPerm in targetPermissions)
        {
            callerPermissions.TryGetValue(targetPerm.Module, out var callerPerm);

            if (targetPerm.CanView && (callerPerm == null || !callerPerm.CanView))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing View permission on {targetPerm.Module}.", "privilege_escalation");

            if (targetPerm.CanCreate && (callerPerm == null || !callerPerm.CanCreate))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing Create permission on {targetPerm.Module}.", "privilege_escalation");

            if (targetPerm.CanEdit && (callerPerm == null || !callerPerm.CanEdit))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing Edit permission on {targetPerm.Module}.", "privilege_escalation");

            if (targetPerm.CanDelete && (callerPerm == null || !callerPerm.CanDelete))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing Delete permission on {targetPerm.Module}.", "privilege_escalation");

            if (targetPerm.CanExport && (callerPerm == null || !callerPerm.CanExport))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing Export permission on {targetPerm.Module}.", "privilege_escalation");

            if (targetPerm.CanImport && (callerPerm == null || !callerPerm.CanImport))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing Import permission on {targetPerm.Module}.", "privilege_escalation");

            if (callerPerm != null && targetPerm.Scope > callerPerm.Scope)
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': requested scope ({targetPerm.Scope}) exceeds your scope ({callerPerm.Scope}) on {targetPerm.Module}.", "privilege_escalation");
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
        u.CreatedAt,
        u.MustChangePassword);
}
