using System.Globalization;
using System.Text.Json;
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

        var limit = workspace.MaxUsersOverride ?? plan.MaxUsers;
        if (activeUsers + pending >= limit)
        {
            throw new PlanLimitException(
                "plan_limit_users",
                workspace.MaxUsersOverride is null
                    ? $"Your {plan.Name} plan allows {limit} users. Upgrade your plan to add more people."
                    : $"Your company is limited to {limit} users. Contact support to add more people.");
        }
    }
}

/// <summary>Reads the audit log (plan 4.5). Admin only at the API layer.</summary>
public sealed class AuditLogQuery(AppDbContext db)
{
    private const int MaxPageSize = 200;

    private IQueryable<AuditLog> BuildQuery(string? entityType, Guid? entityId, string? action, Guid? userId, string? search, DateTime? from, DateTime? to)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var et = entityType.Trim().ToLowerInvariant();
            query = query.Where(a => a.EntityType.ToLower() == et);
        }

        if (entityId is { } id)
        {
            query = query.Where(a => a.EntityId == id);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var act = action.Trim().ToLowerInvariant();
            query = query.Where(a => a.Action.ToLower() == act);
        }

        if (userId is { } uid)
        {
            query = query.Where(a => a.UserId == uid);
        }

        if (from is { } start)
        {
            query = query.Where(a => a.OccurredAt >= DateTime.SpecifyKind(start.Date, DateTimeKind.Utc));
        }

        if (to is { } end)
        {
            query = query.Where(a => a.OccurredAt < DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(a => a.EntityType.ToLower().Contains(term)
                || (a.Changes != null && a.Changes.ToLower().Contains(term))
                || a.Action.ToLower().Contains(term));
        }

        return query;
    }

    public async Task<PagedResult<AuditLogDto>> ListAsync(
        string? entityType,
        Guid? entityId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        string? action = null,
        Guid? userId = null,
        string? search = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = BuildQuery(entityType, entityId, action, userId, search, from, to);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(a => a.OccurredAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim(), u.Email })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var items = rows.Select(a =>
        {
            var user = a.UserId.HasValue ? users.GetValueOrDefault(a.UserId.Value) : null;
            var userName = user?.Name ?? (a.UserId.HasValue ? "Unknown user" : "System / Automated");
            var userEmail = user?.Email;
            var (summary, diff) = ParseChanges(a.Action, a.EntityType, a.Changes);

            return new AuditLogDto(
                a.Id,
                a.UserId,
                a.EntityType,
                a.EntityId,
                a.Action,
                a.Changes,
                a.OccurredAt,
                userName,
                userEmail,
                summary,
                diff);
        }).ToList();

        return new PagedResult<AuditLogDto>(items, total, page, pageSize);
    }

    public async Task<string> ExportAsync(
        string? entityType,
        string? action,
        Guid? userId,
        string? search,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(entityType, null, action, userId, search, from, to);
        var rows = await query.OrderByDescending(a => a.OccurredAt).Take(5000).ToListAsync(cancellationToken);

        var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim(), u.Email })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        static string Cell(string? v)
        {
            v ??= "";
            if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        var sb = new System.Text.StringBuilder("When (UTC),Who,User Email,Action,Module,Record ID,What changed\r\n");
        foreach (var a in rows)
        {
            var user = a.UserId.HasValue ? users.GetValueOrDefault(a.UserId.Value) : null;
            var userName = user?.Name ?? (a.UserId.HasValue ? "Unknown user" : "System");
            var userEmail = user?.Email ?? "";
            var (summary, _) = ParseChanges(a.Action, a.EntityType, a.Changes);

            sb.Append(string.Join(',',
                Cell(a.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Cell(userName),
                Cell(userEmail),
                Cell(a.Action),
                Cell(a.EntityType),
                Cell(a.EntityId.ToString()),
                Cell(summary)
            )).Append("\r\n");
        }

        return sb.ToString();
    }

    private static (string Summary, IReadOnlyList<AuditFieldDiff> Diff) ParseChanges(string action, string entityType, string? changesJson)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
        {
            var defaultSummary = action switch
            {
                "Create" => $"Created new {entityType}",
                "Delete" => $"Deleted {entityType}",
                _ => $"{action} on {entityType}"
            };
            return (defaultSummary, Array.Empty<AuditFieldDiff>());
        }

        try
        {
            using var doc = JsonDocument.Parse(changesJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var diffs = new List<AuditFieldDiff>();
                var fieldNames = new List<string>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var field = item.TryGetProperty("Field", out var f) ? f.GetString() : (item.TryGetProperty("field", out var f2) ? f2.GetString() : null);
                    if (string.IsNullOrEmpty(field)) continue;

                    string? oldVal = item.TryGetProperty("Old", out var o) ? o.ToString() : (item.TryGetProperty("old", out var o2) ? o2.ToString() : null);
                    string? newVal = item.TryGetProperty("New", out var n) ? n.ToString() : (item.TryGetProperty("new", out var n2) ? n2.ToString() : null);

                    diffs.Add(new AuditFieldDiff(field, oldVal, newVal));
                    fieldNames.Add(field);
                }

                if (diffs.Count > 0)
                {
                    var fieldsSummary = string.Join(", ", fieldNames.Take(4));
                    if (fieldNames.Count > 4) fieldsSummary += $" (+{fieldNames.Count - 4} more)";
                    var summary = $"{action} {entityType}: changed {fieldsSummary}";
                    return (summary, diffs);
                }
            }
        }
        catch
        {
            // fallback if JSON parsing fails
        }

        return ($"{action} on {entityType}", Array.Empty<AuditFieldDiff>());
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
