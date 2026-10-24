using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Caching;

/// <summary>What every request needs to know about the signed-in person: whether they can still use the app, their role, and their session stamp.</summary>
public sealed record UserState(Guid TenantId, bool IsActive, Guid? RoleId, string? SecurityStamp);

/// <summary>Whether a company may make changes: its status and when its trial ends.</summary>
public sealed record TenantStanding(string Status, DateTime? TrialEndsAt);

/// <summary>
/// The few facts every request checks (the person, the company's standing, which modules are on), kept for a minute instead of read from the
/// database on every call. Anything that changes them is dropped from here the moment it is saved (<see cref="CacheInvalidationInterceptor"/>).
/// </summary>
public sealed class RequestStateCache(AppDbContext db, HybridCache cache)
{
    private static readonly HybridCacheEntryOptions Keep = new() { Expiration = TimeSpan.FromSeconds(60), LocalCacheExpiration = TimeSpan.FromSeconds(60) };

    public static string UserKey(Guid userId) => $"state:user:{userId}";
    public static string TenantKey(Guid tenantId) => $"state:tenant:{tenantId}";
    public static string ModulesKey(Guid tenantId) => $"state:modules:{tenantId}";

    public ValueTask<UserState?> UserAsync(Guid userId, CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(UserKey(userId), async token =>
            await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new UserState(u.TenantId, u.IsActive, u.RoleId, u.SecurityStamp)).FirstOrDefaultAsync(token),
            Keep, cancellationToken: cancellationToken);

    public ValueTask<TenantStanding?> TenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(TenantKey(tenantId), async token =>
            await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
                .Select(t => new TenantStanding(t.Status, t.TrialEndsAt)).FirstOrDefaultAsync(token),
            Keep, cancellationToken: cancellationToken);

    /// <summary>The company's own on/off switches by module name. A module with no row is on.</summary>
    public ValueTask<Dictionary<string, bool>> ModulesAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(ModulesKey(tenantId), async token =>
            await db.ModuleSettings.IgnoreQueryFilters().AsNoTracking().Where(m => m.TenantId == tenantId && !m.IsDeleted)
                .ToDictionaryAsync(m => m.Module.ToString(), m => m.IsEnabled, token),
            Keep, cancellationToken: cancellationToken);
}
