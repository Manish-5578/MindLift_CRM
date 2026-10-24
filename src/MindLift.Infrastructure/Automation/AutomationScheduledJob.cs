using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Automation;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Common;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;

namespace MindLift.Infrastructure.Automation;

/// <summary>
/// Executes delayed automation actions scheduled via Hangfire (Zoho Scheduled Actions).
/// Verifies the record still exists, is not deleted, and still matches conditions before running.
/// </summary>
public sealed class AutomationScheduledJob(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    IEmailSender emailSender,
    MindLift.Infrastructure.Messaging.EmailGuard emailGuard,
    INotificationService notifications,
    IHttpClientFactory? httpClientFactory,
    ILogger<AutomationScheduledJob> logger
)
{
    public async Task ExecuteScheduledAsync(
        Guid tenantId,
        Guid ruleId,
        string entityType,
        Guid entityId,
        string serializedAction,
        CancellationToken cancellationToken = default
    )
    {
        // An action scheduled before the workspace lapsed does not run after it has.
        var workspace = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => new { t.Status, t.TrialEndsAt }).FirstOrDefaultAsync(cancellationToken);
        if (workspace is null || MindLift.Infrastructure.Tenancy.TenantAccess.IsLapsed(workspace.Status, workspace.TrialEndsAt, clock.UtcNow))
        {
            logger.LogInformation("Scheduled action dropped: the workspace has lapsed.");
            return;
        }

        using var scope = tenant.Use(tenantId);

        var rule = await db.AutomationRules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null || !rule.IsActive)
        {
            logger.LogInformation("Scheduled action dropped: rule {RuleId} no longer active or exists.", ruleId);
            return;
        }

        var entity = await LoadEntityAsync(entityType, entityId, cancellationToken);
        if (entity is null || entity.IsDeleted)
        {
            logger.LogInformation("Scheduled action dropped: {EntityType} {EntityId} no longer exists or is deleted.", entityType, entityId);
            return;
        }

        // Re-evaluate condition: if the record was edited and no longer matches, drop the scheduled action.
        if (!MatchesConditions(rule, entityType, entity))
        {
            logger.LogInformation("Scheduled action cancelled: {EntityType} {EntityId} no longer matches rule '{RuleName}'.",
                entityType, entityId, rule.Name);

            db.AutomationExecutionLogs.Add(new AutomationExecutionLog
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                EntityType = entityType,
                EntityId = entityId,
                TriggerType = "ScheduledExecution",
                Status = "Cancelled",
                ErrorMessage = "Record no longer matches rule conditions.",
                ExecutedAt = clock.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var action = JsonSerializer.Deserialize<AutomationActionDefinition>(serializedAction);
        if (action is null)
        {
            return;
        }

        try
        {
            await ExecuteSingleActionAsync(rule, entityType, entity, action, cancellationToken);

            db.AutomationExecutionLogs.Add(new AutomationExecutionLog
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                EntityType = entityType,
                EntityId = entityId,
                TriggerType = "ScheduledExecution",
                Status = "Success",
                ExecutedAt = clock.UtcNow,
            });

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Scheduled action for rule '{RuleName}' executed successfully on {EntityType} {EntityId}.",
                rule.Name, entityType, entityId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute scheduled action for rule '{RuleName}' on {EntityType} {EntityId}",
                rule.Name, entityType, entityId);

            db.AutomationExecutionLogs.Add(new AutomationExecutionLog
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                EntityType = entityType,
                EntityId = entityId,
                TriggerType = "ScheduledExecution",
                Status = "Failed",
                ErrorMessage = ex.Message,
                ExecutedAt = clock.UtcNow,
            });

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<TenantEntity?> LoadEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        return entityType.ToLowerInvariant() switch
        {
            "leads" => await db.Leads.FirstOrDefaultAsync(l => l.Id == entityId, cancellationToken),
            "contacts" => await db.Contacts.FirstOrDefaultAsync(c => c.Id == entityId, cancellationToken),
            "accounts" => await db.Accounts.FirstOrDefaultAsync(a => a.Id == entityId, cancellationToken),
            "deals" => await db.Deals.FirstOrDefaultAsync(d => d.Id == entityId, cancellationToken),
            "activities" => await db.Activities.FirstOrDefaultAsync(a => a.Id == entityId, cancellationToken),
            _ => null,
        };
    }

    private bool MatchesConditions(AutomationRule rule, string entityType, TenantEntity entity)
    {
        if (rule.Conditions is null)
        {
            return true;
        }

        try
        {
            var group = JsonSerializer.Deserialize<FilterGroup>(rule.Conditions.RootElement.GetRawText()) ?? new FilterGroup();
            if (group.Conditions.Count == 0 && group.Groups.Count == 0)
            {
                return true;
            }

            var entityInfo = SystemFields.Get(entityType);
            var context = new FilterContext(TimeZoneInfo.Utc, clock.UtcNow, DayOfWeek.Monday, 1, null);
            var method = typeof(FilterCompiler).GetMethod(nameof(FilterCompiler.Compile))!
                .MakeGenericMethod(entity.GetType());
            var lambda = (LambdaExpression)method.Invoke(null, [group, entityInfo.SystemFields, context])!;
            var compiled = (Delegate)lambda.Compile();
            return (bool)compiled.DynamicInvoke(entity)!;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to evaluate scheduled condition for rule '{RuleName}'", rule.Name);
            return false;
        }
    }

    private async Task ExecuteSingleActionAsync(
        AutomationRule rule,
        string entityType,
        TenantEntity entity,
        AutomationActionDefinition action,
        CancellationToken cancellationToken
    )
    {
        switch (action.Type)
        {
            case AutomationActionType.CreateTask:
                var ownerId = (entity as IOwnedEntity)?.OwnerId ?? Guid.Empty;
                db.Activities.Add(new Activity
                {
                    Type = "Task",
                    Subject = action.Subject ?? $"Follow up on {entityType}",
                    Priority = action.Priority ?? "Normal",
                    Status = "Open",
                    DueAt = clock.UtcNow.AddDays(action.OffsetDays ?? 1),
                    OwnerId = ownerId != Guid.Empty ? ownerId : tenant.UserId ?? Guid.Empty,
                    LeadId = entity is Lead l ? l.Id : null,
                    ContactId = entity is Contact c ? c.Id : null,
                    AccountId = entity is Account a ? a.Id : null,
                    DealId = entity is Deal d ? d.Id : null,
                });
                break;

            case AutomationActionType.SendNotification:
                var notifOwner = (entity as IOwnedEntity)?.OwnerId;
                if (notifOwner is not null && notifOwner != Guid.Empty)
                {
                    await notifications.NotifyAsync(new NotificationRequest(
                        UserIds: [notifOwner.Value],
                        Type: "Automation",
                        Title: rule.Name,
                        Body: action.NotificationMessage ?? $"Scheduled action for '{rule.Name}' ran on {entityType}.",
                        EntityType: entityType,
                        EntityId: entity.Id,
                        Url: $"/{entityType}/{entity.Id}",
                        IncludeActor: true,
                        InAppOnly: true
                    ), cancellationToken);
                }
                break;

            case AutomationActionType.SendEmail:
                var recipient = action.EmailRecipient ?? (entity as Lead)?.Email ?? (entity as Contact)?.Email;
                if (!string.IsNullOrWhiteSpace(recipient))
                {
                    var subject = action.Subject ?? $"Update from MindLift regarding {entityType}";
                    var body = action.EmailBody ?? action.NotificationMessage ?? $"Automation rule '{rule.Name}' triggered.";
                    var (allowed, _) = await emailGuard.TryReserveAsync(recipient, cancellationToken);
                    if (allowed)
                    {
                        await emailSender.SendAsync(recipient, subject, body + emailGuard.Footer(recipient), cancellationToken);
                    }
                }
                break;
        }
    }
}
