using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Tenancy;

/// <summary>
/// What a workspace's standing allows. A workspace has lapsed when it is PastDue, Cancelled or Suspended, or when its trial has ended even if the daily job has not
/// moved it to PastDue yet. Staff can still read their data (so they can see it and pay), but nothing new is accepted: staff writes, public booking, website forms,
/// provider webhooks and the background jobs that send reminders and digests all stop.
/// </summary>
public static class TenantAccess
{
    public static bool IsLapsed(string? status, DateTime? trialEndsAt, DateTime nowUtc)
        => status is "PastDue" or "Cancelled" or "Suspended" || (status == "Trial" && trialEndsAt is { } end && end <= nowUtc);

    public static bool IsSuspended(string? status) => status == "Suspended";

    /// <summary>The ids of workspaces in good standing, as a query a job can use inside its own, so a lapsed workspace's rows never use up a batch.</summary>
    public static IQueryable<Guid> InGoodStanding(IQueryable<Tenant> tenants, DateTime nowUtc)
        => tenants.Where(t => t.Status == "Active" || (t.Status == "Trial" && (t.TrialEndsAt == null || t.TrialEndsAt > nowUtc))).Select(t => t.Id);
}

/// <summary>Looks up whether one workspace has lapsed, for the places that learn the workspace only after the request has started.</summary>
public sealed class TenantStanding(AppDbContext db, IClock clock)
{
    public async Task<bool> IsLapsedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var workspace = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => new { t.Status, t.TrialEndsAt }).FirstOrDefaultAsync(cancellationToken);
        return workspace is null || TenantAccess.IsLapsed(workspace.Status, workspace.TrialEndsAt, clock.UtcNow);
    }

    public async Task<bool> IsSuspendedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var status = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Status).FirstOrDefaultAsync(cancellationToken);
        return status is null || TenantAccess.IsSuspended(status);
    }
}
