using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Platform;

/// <summary>
/// Reads what the platform owner allows the current company. The answer is kept for a few minutes and dropped at once when the owner changes it,
/// so a module switched off in the platform console is refused on the very next request while ordinary requests do not touch the database for it.
/// </summary>
public sealed class PlatformEntitlements(AppDbContext db, ITenantContext tenant, HybridCache cache) : IPlatformEntitlements
{
    /// <summary>What is kept for one company.</summary>
    public sealed record Snapshot(string[] DisabledModules, bool AiAllowed);

    private static readonly HybridCacheEntryOptions Keep = new() { Expiration = TimeSpan.FromMinutes(10), LocalCacheExpiration = TimeSpan.FromMinutes(1) };

    private static string Key(Guid tenantId) => $"platform:entitlements:{tenantId}";

    private async Task<Snapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return null;
        }

        return await cache.GetOrCreateAsync(
            Key(tenantId),
            async token =>
            {
                var row = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId)
                    .Select(t => new { t.PlatformDisabledModules, t.AiReadingAllowed, t.PlanId }).FirstOrDefaultAsync(token);
                if (row is null)
                {
                    return new Snapshot([], true);
                }

                var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == row.PlanId, token);
                return new Snapshot(row.PlatformDisabledModules.Concat(PlanModules.Excluded(plan)).Distinct(StringComparer.Ordinal).ToArray(), row.AiReadingAllowed);
            },
            Keep,
            cancellationToken: cancellationToken);
    }

    public async Task<bool> IsModuleAllowedAsync(AppModule module, CancellationToken cancellationToken = default)
    {
        // Core modules are how a company runs its own workspace; they are never taken away.
        if (ModuleCatalog.Find(module) is { Core: true })
        {
            return true;
        }

        var snapshot = await LoadAsync(cancellationToken);
        return snapshot is null || !snapshot.DisabledModules.Contains(module.ToString(), StringComparer.Ordinal);
    }

    public async Task<IReadOnlySet<AppModule>> DisabledModulesAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadAsync(cancellationToken);
        var set = new HashSet<AppModule>();
        foreach (var name in snapshot?.DisabledModules ?? [])
        {
            if (Enum.TryParse<AppModule>(name, out var module) && ModuleCatalog.Find(module) is { Core: false })
            {
                set.Add(module);
            }
        }

        return set;
    }

    public async Task<bool> IsAiAllowedAsync(CancellationToken cancellationToken = default)
        => (await LoadAsync(cancellationToken))?.AiAllowed ?? true;

    public ValueTask InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => cache.RemoveAsync(Key(tenantId), cancellationToken);
}
