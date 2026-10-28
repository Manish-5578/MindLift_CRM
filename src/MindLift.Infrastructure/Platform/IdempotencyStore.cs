using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Platform;

/// <summary>
/// Backed by a unique index on (company, scope, key): the database, not the application, decides who got there first.
/// An expired record is cleared before a new one is tried, so a key can be reused after its time to live.
/// </summary>
public sealed class IdempotencyStore(AppDbContext db, ITenantContext tenant, IClock clock) : IIdempotencyStore
{
    private const int MaxScope = 64;
    private const int MaxKey = 200;

    private Guid CurrentTenantId => tenant.TenantId ?? throw new ForbiddenException();

    public async Task<bool> TryBeginAsync(string scope, string key, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        Check(scope, key);
        if (timeToLive <= TimeSpan.Zero || timeToLive > TimeSpan.FromDays(90))
        {
            throw new ValidationException("timeToLive", "Choose a time to live between a moment and 90 days.");
        }

        var tenantId = CurrentTenantId;
        var now = clock.UtcNow;
        await db.Set<IdempotencyRecord>().Where(r => r.Scope == scope && r.Key == key && r.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);

        var record = new IdempotencyRecord { TenantId = tenantId, Scope = scope, Key = key, ExpiresAt = now.Add(timeToLive) };
        db.Set<IdempotencyRecord>().Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            db.Entry(record).State = EntityState.Detached;
            return false;
        }
    }

    public async Task CompleteAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        Check(scope, key);
        var now = clock.UtcNow;
        await db.Set<IdempotencyRecord>().Where(r => r.Scope == scope && r.Key == key).ExecuteUpdateAsync(s => s.SetProperty(r => r.CompletedAt, now), cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        return await db.Set<IdempotencyRecord>().IgnoreQueryFilters().Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
    }

    private static void Check(string scope, string key)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > MaxScope)
        {
            throw new ValidationException("scope", $"A scope of 1 to {MaxScope} characters is required.");
        }

        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKey)
        {
            throw new ValidationException("key", $"A key of 1 to {MaxKey} characters is required.");
        }
    }
}

/// <summary>Clears expired idempotency records for every company once a day.</summary>
public sealed class IdempotencyCleanupJob(IIdempotencyStore store, Microsoft.Extensions.Logging.ILogger<IdempotencyCleanupJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var removed = await store.PurgeExpiredAsync(cancellationToken);
        if (removed > 0)
        {
            logger.LogInformation("Removed {Count} expired idempotency records", removed);
        }
    }
}
