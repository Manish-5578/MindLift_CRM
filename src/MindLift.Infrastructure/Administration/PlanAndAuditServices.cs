using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Administration;

/// <summary>Counts active seats (active users plus pending invitations) against the tenant's plan (plan 6.9).</summary>
public sealed class PlanLimitService(AppDbContext db, ITenantContext tenant, IClock clock) : IPlanLimitService
{
    public async Task EnsureSeatAvailableAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No workspace is selected.");
        var workspace = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, cancellationToken);
        var plan = await db.Plans.AsNoTracking().SingleAsync(p => p.Id == workspace.PlanId, cancellationToken);

        var now = clock.UtcNow;
        var activeUsers = await db.Users.AsNoTracking().CountAsync(u => u.IsActive, cancellationToken);
        var pending = await db.Invitations.AsNoTracking()
            .CountAsync(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now, cancellationToken);

        if (activeUsers + pending >= plan.MaxUsers)
        {
            throw new PlanLimitException(
                "plan_limit_users",
                $"Your {plan.Name} plan allows {plan.MaxUsers} users. Upgrade your plan to add more people.");
        }
    }
}

/// <summary>Reads the audit log (plan 4.5). Admin only at the API layer.</summary>
public sealed class AuditLogQuery(AppDbContext db)
{
    private const int MaxPageSize = 200;

    public async Task<PagedResult<AuditLogDto>> ListAsync(string? entityType, Guid? entityId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        if (entityId is { } id)
        {
            query = query.Where(a => a.EntityId == id);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(a => a.OccurredAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(a => new AuditLogDto(a.Id, a.UserId, a.EntityType, a.EntityId, a.Action, a.Changes, a.OccurredAt)).ToList();
        return new PagedResult<AuditLogDto>(items, total, page, pageSize);
    }
}

/// <summary>Platform-admin operations across tenants (plan 6.10). Only reachable with the platform-admin policy.</summary>
public sealed class PlatformService(AppDbContext db, IClock clock)
{
    private static readonly string[] Statuses = ["Trial", "Active", "PastDue", "Suspended", "Cancelled"];

    public async Task<PagedResult<PlatformTenantDto>> ListTenantsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var query = db.Tenants.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.Name.ToLower().Contains(term) || t.Slug.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var tenants = await query.OrderBy(t => t.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var plans = await db.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Key, cancellationToken);
        var userCounts = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive)
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, cancellationToken);

        var items = tenants.Select(t => new PlatformTenantDto(
            t.Id, t.Name, t.Slug, t.Status,
            plans.GetValueOrDefault(t.PlanId) ?? string.Empty,
            userCounts.GetValueOrDefault(t.Id),
            t.TrialEndsAt, t.CreatedAt)).ToList();

        return new PagedResult<PlatformTenantDto>(items, total, page, pageSize);
    }

    public async Task ChangeStatusAsync(Guid tenantId, string status, CancellationToken cancellationToken = default)
    {
        if (!Statuses.Contains(status))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] });
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", tenantId);
        tenant.Status = status;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangePlanAsync(Guid tenantId, string planKey, CancellationToken cancellationToken = default)
    {
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Key == planKey && p.IsActive, cancellationToken)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["planKey"] = ["Unknown plan."] });
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", tenantId);

        tenant.PlanId = plan.Id;
        await db.SaveChangesAsync(cancellationToken);
    }
}
