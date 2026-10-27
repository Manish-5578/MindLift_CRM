using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Administration;

/// <summary>Roles, the permission matrix and teams (plan 7.1). Changes invalidate the cached matrix.</summary>
public sealed class RoleAdministrationService(AppDbContext db, IPermissionService permissions, ITenantContext tenant)
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var counts = await db.Users.AsNoTracking()
            .Where(u => u.RoleId != null && u.IsActive)
            .GroupBy(u => u.RoleId!.Value)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);

        return roles.Select(r => new RoleDto(
            r.Id, r.Name, r.Description, r.IsSystem, r.IsAdmin, r.DashboardView,
            counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<RoleDto> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureNameFreeAsync(request.Name, null, cancellationToken);

        var role = new Role
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            DashboardView = request.DashboardView,
            IsSystem = false,
            IsAdmin = false,
        };
        db.Roles.Add(role);

        // New roles start with no access. The matrix is filled in on the permissions page.
        foreach (var module in Enum.GetValues<AppModule>())
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Module = module, Scope = DataScope.Own });
        }

        await db.SaveChangesAsync(cancellationToken);
        return new RoleDto(role.Id, role.Name, role.Description, role.IsSystem, role.IsAdmin, role.DashboardView, 0);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        var role = await LoadAsync(id, cancellationToken);
        if (role.IsSystem && !string.Equals(role.Name, request.Name.Trim(), StringComparison.Ordinal))
        {
            throw new ConflictException("system_role", "System roles cannot be renamed.");
        }

        await EnsureNameFreeAsync(request.Name, id, cancellationToken);
        role.Name = request.Name.Trim();
        role.Description = request.Description;
        role.DashboardView = request.DashboardView;
        await db.SaveChangesAsync(cancellationToken);
        await permissions.InvalidateRoleAsync(id, cancellationToken);
        return new RoleDto(role.Id, role.Name, role.Description, role.IsSystem, role.IsAdmin, role.DashboardView, 0);
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

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await IsCallerAdminAsync(cancellationToken))
        {
            throw new ForbiddenException("Only administrators can delete roles.", "admin_required");
        }

        var role = await LoadAsync(id, cancellationToken);
        if (role.IsSystem)
        {
            throw new ConflictException("system_role", "System roles cannot be deleted.");
        }

        if (await db.Users.AnyAsync(u => u.RoleId == id, cancellationToken))
        {
            throw new ConflictException("role_in_use", "Move the users of this role to another role before deleting it.");
        }

        var rows = await db.RolePermissions.Where(p => p.RoleId == id).ToListAsync(cancellationToken);
        db.RolePermissions.RemoveRange(rows);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        await permissions.InvalidateRoleAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<RolePermissionDto>> GetPermissionsAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        await LoadAsync(roleId, cancellationToken);
        var rows = await db.RolePermissions.AsNoTracking().Where(p => p.RoleId == roleId).ToListAsync(cancellationToken);

        return Enum.GetValues<AppModule>().Select(module =>
        {
            var row = rows.FirstOrDefault(r => r.Module == module);
            return row is null
                ? new RolePermissionDto(module, false, false, false, false, false, false, DataScope.Own)
                : new RolePermissionDto(module, row.CanView, row.CanCreate, row.CanEdit, row.CanDelete, row.CanExport, row.CanImport, row.Scope);
        }).ToList();
    }

    public async Task<IReadOnlyList<RolePermissionDto>> SavePermissionsAsync(Guid roleId, IReadOnlyList<RolePermissionDto> matrix, CancellationToken cancellationToken = default)
    {
        if (!await IsCallerAdminAsync(cancellationToken))
        {
            throw new ForbiddenException("Only administrators can modify role permissions.", "admin_required");
        }

        var role = await LoadAsync(roleId, cancellationToken);
        if (role.IsAdmin)
        {
            throw new ConflictException("admin_permissions_fixed", "The Admin role always has full access.");
        }

        var existing = await db.RolePermissions.Where(p => p.RoleId == roleId).ToListAsync(cancellationToken);
        foreach (var item in matrix)
        {
            var row = existing.FirstOrDefault(r => r.Module == item.Module);
            if (row is null)
            {
                row = new RolePermission { RoleId = roleId, Module = item.Module };
                db.RolePermissions.Add(row);
            }

            row.CanView = item.CanView;
            row.CanCreate = item.CanCreate;
            row.CanEdit = item.CanEdit;
            row.CanDelete = item.CanDelete;
            row.CanExport = item.CanExport;
            row.CanImport = item.CanImport;
            row.Scope = item.Scope;
        }

        await db.SaveChangesAsync(cancellationToken);
        await permissions.InvalidateRoleAsync(roleId, cancellationToken);
        return await GetPermissionsAsync(roleId, cancellationToken);
    }

    public async Task<RoleDto> CloneAsync(Guid roleId, string newName, CancellationToken cancellationToken = default)
    {
        if (!await IsCallerAdminAsync(cancellationToken))
        {
            throw new ForbiddenException("Only administrators can clone roles.", "admin_required");
        }

        var source = await LoadAsync(roleId, cancellationToken);
        var matrix = await GetPermissionsAsync(roleId, cancellationToken);
        var clone = await CreateAsync(new SaveRoleRequest(newName, $"Copy of {source.Name}", source.DashboardView), cancellationToken);
        await SavePermissionsAsync(clone.Id, matrix, cancellationToken);
        return clone;
    }

    private async Task<Role> LoadAsync(Guid id, CancellationToken cancellationToken)
        => await db.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Role", id);

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        var taken = await db.Roles.AnyAsync(r => r.Name == trimmed && r.Id != exceptId, cancellationToken);
        if (taken)
        {
            throw new ConflictException("role_name_taken", "A role with this name already exists.");
        }
    }
}

/// <summary>Teams and branches (plan 7.1).</summary>
public sealed class TeamBranchService(AppDbContext db)
{
    public async Task<IReadOnlyList<TeamDto>> ListTeamsAsync(CancellationToken cancellationToken = default)
    {
        var teams = await db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var counts = await db.Users.AsNoTracking()
            .Where(u => u.TeamId != null)
            .GroupBy(u => u.TeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, cancellationToken);

        return teams.Select(t => new TeamDto(t.Id, t.Name, t.BranchId, t.LeaderId, counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<TeamDto> CreateTeamAsync(SaveTeamRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureTeamNameFreeAsync(request.Name, null, cancellationToken);
        await EnsureBranchExistsAsync(request.BranchId, cancellationToken);
        await EnsureLeaderExistsAsync(request.LeaderId, cancellationToken);

        var team = new Team { Name = request.Name.Trim(), BranchId = request.BranchId, LeaderId = request.LeaderId };
        db.Teams.Add(team);
        await db.SaveChangesAsync(cancellationToken);
        return new TeamDto(team.Id, team.Name, team.BranchId, team.LeaderId, 0);
    }

    public async Task<TeamDto> UpdateTeamAsync(Guid id, SaveTeamRequest request, CancellationToken cancellationToken = default)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException("Team", id);
        await EnsureTeamNameFreeAsync(request.Name, id, cancellationToken);
        await EnsureBranchExistsAsync(request.BranchId, cancellationToken);
        await EnsureLeaderExistsAsync(request.LeaderId, cancellationToken);

        team.Name = request.Name.Trim();
        team.BranchId = request.BranchId;
        team.LeaderId = request.LeaderId;
        await db.SaveChangesAsync(cancellationToken);

        var members = await db.Users.CountAsync(u => u.TeamId == id, cancellationToken);
        return new TeamDto(team.Id, team.Name, team.BranchId, team.LeaderId, members);
    }

    public async Task DeleteTeamAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException("Team", id);
        if (await db.Users.AnyAsync(u => u.TeamId == id, cancellationToken))
        {
            throw new ConflictException("team_in_use", "Move the members of this team to another team before deleting it.");
        }

        db.Teams.Remove(team);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BranchDto>> ListBranchesAsync(CancellationToken cancellationToken = default)
        => (await db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync(cancellationToken))
            .Select(b => new BranchDto(b.Id, b.Name, b.Address, b.TimeZoneId)).ToList();

    public async Task<BranchDto> CreateBranchAsync(SaveBranchRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureBranchNameFreeAsync(request.Name, null, cancellationToken);
        EnsureTimeZone(request.TimeZoneId);
        var branch = new Branch { Name = request.Name.Trim(), Address = request.Address, TimeZoneId = request.TimeZoneId };
        db.Branches.Add(branch);
        await db.SaveChangesAsync(cancellationToken);
        return new BranchDto(branch.Id, branch.Name, branch.Address, branch.TimeZoneId);
    }

    public async Task<BranchDto> UpdateBranchAsync(Guid id, SaveBranchRequest request, CancellationToken cancellationToken = default)
    {
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken) ?? throw new NotFoundException("Branch", id);
        await EnsureBranchNameFreeAsync(request.Name, id, cancellationToken);
        EnsureTimeZone(request.TimeZoneId);
        branch.Name = request.Name.Trim();
        branch.Address = request.Address;
        branch.TimeZoneId = request.TimeZoneId;
        await db.SaveChangesAsync(cancellationToken);
        return new BranchDto(branch.Id, branch.Name, branch.Address, branch.TimeZoneId);
    }

    public async Task DeleteBranchAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken) ?? throw new NotFoundException("Branch", id);
        if (await db.Teams.AnyAsync(t => t.BranchId == id, cancellationToken))
        {
            throw new ConflictException("branch_in_use", "Move the teams of this branch before deleting it.");
        }

        db.Branches.Remove(branch);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureTeamNameFreeAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        if (await db.Teams.AnyAsync(t => t.Name == trimmed && t.Id != exceptId, cancellationToken))
        {
            throw new ConflictException("team_name_taken", "A team with this name already exists.");
        }
    }

    private async Task EnsureBranchNameFreeAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        if (await db.Branches.AnyAsync(b => b.Name == trimmed && b.Id != exceptId, cancellationToken))
        {
            throw new ConflictException("branch_name_taken", "A branch with this name already exists.");
        }
    }

    /// <summary>A leader must be an active person in this workspace. The tenant filter on users keeps other workspaces out.</summary>
    private async Task EnsureLeaderExistsAsync(Guid? leaderId, CancellationToken cancellationToken)
    {
        if (leaderId is { } id && !await db.Users.AnyAsync(u => u.Id == id && u.IsActive, cancellationToken))
        {
            throw new NotFoundException("User", id);
        }
    }

    /// <summary>A branch may have no time zone of its own (it then uses the company's). If it has one, it has to exist.</summary>
    private static void EnsureTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && !TimeZoneRules.Exists(timeZoneId))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["timeZoneId"] = ["Unknown time zone."] });
        }
    }

    private async Task EnsureBranchExistsAsync(Guid? branchId, CancellationToken cancellationToken)
    {
        if (branchId is { } id && !await db.Branches.AnyAsync(b => b.Id == id, cancellationToken))
        {
            throw new NotFoundException("Branch", id);
        }
    }
}
