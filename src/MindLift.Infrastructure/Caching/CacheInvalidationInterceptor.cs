using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Identity;

namespace MindLift.Infrastructure.Caching;

/// <summary>
/// Drops what <see cref="RequestStateCache"/> remembered as soon as the underlying rows are saved, so a person who is deactivated,
/// a company that is suspended or a module that is switched off takes effect on the very next request.
/// </summary>
public sealed class CacheInvalidationInterceptor(HybridCache cache) : SaveChangesInterceptor
{
    private readonly HashSet<string> _pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        FlushAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case AppUser user:
                    _pending.Add(RequestStateCache.UserKey(user.Id));
                    break;
                case Tenant company:
                    _pending.Add(RequestStateCache.TenantKey(company.Id));
                    _pending.Add($"platform:entitlements:{company.Id}");
                    break;
                case ModuleSetting module:
                    _pending.Add(RequestStateCache.ModulesKey(module.TenantId));
                    break;
            }
        }
    }

    private async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var keys = _pending.ToList();
        _pending.Clear();
        await cache.RemoveAsync(keys, cancellationToken);
    }
}
