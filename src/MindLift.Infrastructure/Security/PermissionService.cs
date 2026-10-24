using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Identity;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Security;

/// <summary>
/// Resolves the current user's permissions from their role (plan 4.4.4).
/// Role matrices are cached per tenant and invalidated when a role changes (plan 4.3).
/// Module enablement is checked on every call, so disabling a module takes effect immediately.
/// </summary>
public sealed class PermissionService(IAppDbContext db, ITenantContext tenant, HybridCache cache, MindLift.Infrastructure.Settings.SettingsVersion settingsVersion, IPlatformEntitlements entitlements, MindLift.Infrastructure.Caching.RequestStateCache state) : IPermissionService
{
    /// <summary>Cached form of one role's permissions.</summary>
    private sealed record RoleMatrix(bool IsAdmin, List<ModulePermission> Modules);

    public async Task<bool> HasAsync(AppModule module, PermissionAction action, CancellationToken cancellationToken = default)
    {
        var permission = await GetModulePermissionAsync(module, cancellationToken);
        return permission is not null && Allows(permission, action);
    }

    public async Task<ModulePermission?> GetModulePermissionAsync(AppModule module, CancellationToken cancellationToken = default)
    {
        if (!await IsModuleEnabledAsync(module, cancellationToken))
        {
            return null;
        }

        var user = await CurrentUserAsync(cancellationToken);
        if (user is null || !user.IsActive || user.RoleId is null)
        {
            return null;
        }

        var matrix = await LoadRoleMatrixAsync(user.RoleId.Value, cancellationToken);
        if (matrix.IsAdmin)
        {
            return new ModulePermission(module, true, true, true, true, true, true, DataScope.All);
        }

        return matrix.Modules.FirstOrDefault(m => m.Module == module);
    }

    public async Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        await cache.RemoveAsync(CacheKey(roleId), cancellationToken);
        await settingsVersion.BumpAsync(cancellationToken);
    }

    private static bool Allows(ModulePermission permission, PermissionAction action) => action switch
    {
        PermissionAction.View => permission.CanView,
        PermissionAction.Create => permission.CanCreate,
        PermissionAction.Edit => permission.CanEdit,
        PermissionAction.Delete => permission.CanDelete,
        PermissionAction.Export => permission.CanExport,
        PermissionAction.Import => permission.CanImport,
        _ => false,
    };

    private string CacheKey(Guid roleId) => $"{tenant.TenantId}:perm:{roleId}";

    private async Task<RoleMatrix> LoadRoleMatrixAsync(Guid roleId, CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync(
            CacheKey(roleId),
            async token =>
            {
                var role = await db.Set<Role>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, token);
                if (role is null)
                {
                    return new RoleMatrix(false, []);
                }

                var rows = await db.Set<RolePermission>().AsNoTracking()
                    .Where(p => p.RoleId == roleId)
                    .ToListAsync(token);

                var modules = rows.Select(p => new ModulePermission(
                    p.Module, p.CanView, p.CanCreate, p.CanEdit, p.CanDelete, p.CanExport, p.CanImport, p.Scope)).ToList();

                return new RoleMatrix(role.IsAdmin, modules);
            },
            cancellationToken: cancellationToken);
    }

    private async Task<MindLift.Infrastructure.Caching.UserState?> CurrentUserAsync(CancellationToken cancellationToken)
    {
        if (tenant.UserId is not { } userId)
        {
            return null;
        }

        var user = await state.UserAsync(userId, cancellationToken);
        return user is not null && user.TenantId == tenant.TenantId ? user : null;
    }

    private async Task<bool> IsModuleEnabledAsync(AppModule module, CancellationToken cancellationToken)
    {
        // The platform owner's switch comes first: a module taken away from the company is unavailable whatever the company's own settings say.
        if (!await entitlements.IsModuleAllowedAsync(module, cancellationToken))
        {
            return false;
        }

        if (tenant.TenantId is not { } tenantId)
        {
            return false;
        }

        // A module without a settings row is enabled.
        var switches = await state.ModulesAsync(tenantId, cancellationToken);
        return !switches.TryGetValue(module.ToString(), out var on) || on;
    }
}
