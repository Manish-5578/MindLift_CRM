using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Tenancy;

/// <summary>Hands out the next number for quotes and invoices, one company at a time, without ever repeating one.</summary>
public sealed class NumberSequences(AppDbContext db, ITenantContext tenant)
{
    public const string Quote = "quote";
    public const string Invoice = "invoice";

    /// <summary>
    /// The next number. A company that already has records continues after them: the first call starts from <paramref name="firstValue"/>
    /// (the caller counts what exists, deleted records included), and every later call adds one in the database itself.
    /// </summary>
    public Task<long> NextAsync(string key, Func<Task<long>> firstValue, CancellationToken cancellationToken = default)
        => NextForAsync(tenant.TenantId ?? throw new UnauthorizedAccessException("Tenant context required."), key, firstValue, cancellationToken);

    /// <summary>The platform's own numbers (its invoices to companies) are kept under an empty company id.</summary>
    public Task<long> NextPlatformAsync(string key, CancellationToken cancellationToken = default)
        => NextForAsync(Guid.Empty, key, () => Task.FromResult(1L), cancellationToken);

    private async Task<long> NextForAsync(Guid tenantId, string key, Func<Task<long>> firstValue, CancellationToken cancellationToken)
    {

        // Common case first: the row exists, so one atomic increment is enough.
        var next = await IncrementAsync(tenantId, key, cancellationToken);
        if (next is { } value)
        {
            return value;
        }

        // First use in this company. Two people may get here together, so the insert is guarded; the loser increments instead.
        var seed = await firstValue();
        var inserted = await db.Database.SqlQuery<long>($"""
            INSERT INTO number_sequences (tenant_id, key, last_value) VALUES ({tenantId}, {key}, {seed})
            ON CONFLICT (tenant_id, key) DO UPDATE SET last_value = number_sequences.last_value + 1
            RETURNING last_value AS "Value"
            """).ToListAsync(cancellationToken);
        return inserted.Single();
    }

    private async Task<long?> IncrementAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<long>($"""
            UPDATE number_sequences SET last_value = last_value + 1 WHERE tenant_id = {tenantId} AND key = {key}
            RETURNING last_value AS "Value"
            """).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }
}
