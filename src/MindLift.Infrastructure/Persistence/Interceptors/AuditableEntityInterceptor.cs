using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Common;
using MindLift.Domain.Entities;

namespace MindLift.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Runs before every SaveChanges (plan section 4.1.4):
/// <list type="bullet">
/// <item>Assigns the tenant to new rows and blocks writes to another tenant's rows.</item>
/// <item>Stamps CreatedAt/UpdatedAt and the acting user.</item>
/// <item>Turns deletes of non-hard-delete rows into soft deletes.</item>
/// <item>Writes an audit row for every change to an [Audited] entity.</item>
/// </list>
/// </summary>
public sealed class AuditableEntityInterceptor(ITenantContext tenant, IClock clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> IgnoredAuditFields =
    [
        nameof(TenantEntity.UpdatedAt),
        nameof(TenantEntity.UpdatedById),
    ];

    /// <summary>The audit trail records that a secret changed, never what it was or what it became.</summary>
    private const string Redacted = "[redacted]";

    private static bool IsSecretField(string name)
        => name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Password", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith("TokenHash", StringComparison.OrdinalIgnoreCase)
           || name.Equals("ApiKey", StringComparison.OrdinalIgnoreCase);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ProcessChanges(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ProcessChanges(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void ProcessChanges(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var audits = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries<TenantEntity>().ToList())
        {
            var entity = entry.Entity;

            switch (entry.State)
            {
                case EntityState.Added:
                    AssignTenantForInsert(entity);
                    entity.CreatedAt = now;
                    entity.CreatedById = tenant.UserId;
                    entity.Version = 1;
                    AddAudit(audits, entity, "Create", null, now);
                    break;

                case EntityState.Modified:
                    EnsureSameTenant(entry);
                    var changes = CollectChanges(entry);
                    entity.UpdatedAt = now;
                    entity.UpdatedById = tenant.UserId;
                    entity.Version = Convert.ToInt32(entry.OriginalValues[nameof(TenantEntity.Version)]) + 1;
                    if (changes.Count > 0)
                    {
                        AddAudit(audits, entity, "Update", JsonSerializer.Serialize(changes), now);
                    }

                    break;

                case EntityState.Deleted:
                    EnsureSameTenant(entry);
                    if (HasAttribute<HardDeleteAttribute>(entity))
                    {
                        AddAudit(audits, entity, "Delete", null, now);
                        break;
                    }

                    // Soft delete: keep the row, hide it through the query filter.
                    entry.State = EntityState.Modified;
                    entity.IsDeleted = true;
                    entity.DeletedAt = now;
                    entity.UpdatedAt = now;
                    entity.UpdatedById = tenant.UserId;
                    entity.Version = Convert.ToInt32(entry.OriginalValues[nameof(TenantEntity.Version)]) + 1;
                    AddAudit(audits, entity, "Delete", null, now);
                    break;
            }
        }

        foreach (var audit in audits)
        {
            context.Set<AuditLog>().Add(audit);
        }
    }

    private void AssignTenantForInsert(TenantEntity entity)
    {
        var current = tenant.TenantId ?? throw new ForbiddenException("No tenant is set for this operation.");

        if (entity.TenantId == Guid.Empty)
        {
            entity.TenantId = current;
        }
        else if (entity.TenantId != current && !tenant.IsPlatformAdmin)
        {
            throw new ForbiddenException("Cannot write data for another tenant.");
        }
    }

    private void EnsureSameTenant(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TenantEntity> entry)
    {
        var current = tenant.TenantId ?? throw new ForbiddenException("No tenant is set for this operation.");

        if (entry.Property(nameof(TenantEntity.TenantId)).IsModified
            && !Equals(entry.OriginalValues[nameof(TenantEntity.TenantId)], entry.CurrentValues[nameof(TenantEntity.TenantId)]))
        {
            throw new ForbiddenException("The tenant of a record cannot be changed.");
        }

        if (entry.Entity.TenantId != current && !tenant.IsPlatformAdmin)
        {
            throw new ForbiddenException("Cannot modify data of another tenant.");
        }
    }

    private static List<AuditChange> CollectChanges(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TenantEntity> entry)
    {
        var changes = new List<AuditChange>();
        foreach (var property in entry.Properties)
        {
            if (!property.IsModified || IgnoredAuditFields.Contains(property.Metadata.Name))
            {
                continue;
            }

            var secret = IsSecretField(property.Metadata.Name);
            changes.Add(new AuditChange(
                property.Metadata.Name,
                secret ? Redacted : ToAuditValue(property.OriginalValue),
                secret ? Redacted : ToAuditValue(property.CurrentValue)));
        }

        return changes;
    }

    private void AddAudit(List<AuditLog> audits, TenantEntity entity, string action, string? changes, DateTime now)
    {
        if (!HasAttribute<AuditedAttribute>(entity))
        {
            return;
        }

        audits.Add(new AuditLog
        {
            TenantId = entity.TenantId,
            UserId = tenant.UserId,
            EntityType = entity.GetType().Name,
            EntityId = entity.Id,
            Action = action,
            Changes = changes,
            OccurredAt = now,
        });
    }

    private static bool HasAttribute<TAttribute>(object entity) where TAttribute : Attribute
        => entity.GetType().GetCustomAttribute<TAttribute>(inherit: true) is not null;

    private static object? ToAuditValue(object? value) => value switch
    {
        null => null,
        JsonDocument document => document.RootElement.GetRawText(),
        _ => value,
    };

    private sealed record AuditChange(string Field, object? Old, object? New);
}
