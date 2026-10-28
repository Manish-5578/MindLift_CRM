using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Platform;

/// <summary>
/// Reads and changes the per-company feature switches. The set of switched-on keys is kept for a few minutes per company
/// and dropped the moment a switch changes, so turning a feature off takes effect on the next request.
/// Fails closed: no company, no row or an unknown key all mean OFF.
/// </summary>
public sealed class FeatureFlags(AppDbContext db, ITenantContext tenant, HybridCache cache) : IFeatureFlags
{
    private static readonly HybridCacheEntryOptions Keep = new() { Expiration = TimeSpan.FromMinutes(5), LocalCacheExpiration = TimeSpan.FromMinutes(1) };

    private static string CacheKey(Guid tenantId) => $"features:enabled:{tenantId}";

    private async Task<string[]> EnabledKeysAsync(Guid tenantId, CancellationToken cancellationToken)
        => await cache.GetOrCreateAsync(
            CacheKey(tenantId),
            async token => await db.Set<TenantFeature>().AsNoTracking().Where(f => f.IsEnabled).Select(f => f.Key).ToArrayAsync(token),
            Keep,
            cancellationToken: cancellationToken);

    public async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not { } tenantId || !FeatureCatalog.IsKnown(key))
        {
            return false;
        }

        return (await EnabledKeysAsync(tenantId, cancellationToken)).Contains(key, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<FeatureFlagDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var on = tenant.TenantId is { } tenantId ? await EnabledKeysAsync(tenantId, cancellationToken) : [];
        return FeatureCatalog.All.Select(e => new FeatureFlagDto(e.Key, e.Name, e.Phase, on.Contains(e.Key, StringComparer.Ordinal))).ToList();
    }

    public async Task<FeatureFlagDto> SetAsync(string key, bool enabled, CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            throw new ForbiddenException();
        }

        if (!FeatureCatalog.IsKnown(key))
        {
            throw new ValidationException("key", "Unknown feature.");
        }

        var row = await db.Set<TenantFeature>().FirstOrDefaultAsync(f => f.Key == key, cancellationToken);
        if (row is null)
        {
            db.Set<TenantFeature>().Add(new TenantFeature { TenantId = tenantId, Key = key, IsEnabled = enabled });
        }
        else
        {
            row.IsEnabled = enabled;
        }

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKey(tenantId), cancellationToken);

        var entry = FeatureCatalog.All.First(e => e.Key == key);
        return new FeatureFlagDto(entry.Key, entry.Name, entry.Phase, enabled);
    }
}
