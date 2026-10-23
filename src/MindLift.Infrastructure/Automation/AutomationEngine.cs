using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Automation;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Common;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;

namespace MindLift.Infrastructure.Automation;

public sealed class AutomationEngine(
    AppDbContext db,
    IClock clock,
    ITenantContext tenant,
    INotificationService notifications,
    IEmailSender emailSender,
    IBackgroundJobClient backgroundJobs,
    IAutomationExecutionContext executionContext,
    IHttpClientFactory? httpClientFactory,
    ILogger<AutomationEngine> logger,
    IPlatformEntitlements? entitlements = null
) : IAutomationEngine
{
    public Task ProcessTriggersAsync<T>(
        string entityType,
        AutomationTriggerType triggerType,
        T currentEntity,
        T? previousEntity = null,
        CancellationToken cancellationToken = default
    ) where T : TenantEntity
        => ProcessTriggersAsync(entityType, [triggerType], currentEntity, previousEntity, cancellationToken);

    public async Task ProcessTriggersAsync<T>(
        string entityType,
        IReadOnlyList<AutomationTriggerType> triggerTypes,
        T currentEntity,
        T? previousEntity = null,
        CancellationToken cancellationToken = default
    ) where T : TenantEntity
    {
        // A company the platform owner took Automation away from has no rules run.
        if (entitlements is not null && !await entitlements.IsModuleAllowedAsync(MindLift.Shared.Enums.AppModule.Automation, cancellationToken))
        {
            return;
        }

        if (!executionContext.CanExecute())
        {
            logger.LogWarning("Automation loop protection triggered: max depth {Depth} or max executions {Total} reached. Skipping further rules for {EntityType} {Id}.",
                executionContext.Depth, executionContext.TotalExecutions, entityType, currentEntity.Id);
            return;
        }

        using var _ = executionContext.EnterScope();
        executionContext.RecordExecution();

        var rules = await db.AutomationRules
            .Where(r => r.IsActive && r.EntityType == entityType && triggerTypes.Contains(r.TriggerType))
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            return;
        }

        bool hasChanges = false;
        foreach (var rule in rules)
        {
            try
            {
                if (!MatchesTrigger(rule, currentEntity, previousEntity))
                {
                    continue;
                }

                if (!MatchesConditions(rule, entityType, currentEntity))
                {
                    continue;
                }

                await ExecuteActionsAsync(rule, entityType, currentEntity, cancellationToken);
                hasChanges = true;

                db.AutomationExecutionLogs.Add(new AutomationExecutionLog
                {
                    RuleId = rule.Id,
                    RuleName = rule.Name,
                    EntityType = entityType,
                    EntityId = currentEntity.Id,
                    TriggerType = rule.TriggerType.ToString(),
                    Status = "Success",
                    ExecutedAt = clock.UtcNow,
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Automation rule '{RuleName}' ({RuleId}) failed on {EntityType} {EntityId}",
                    rule.Name, rule.Id, entityType, currentEntity.Id);

                db.AutomationExecutionLogs.Add(new AutomationExecutionLog
                {
                    RuleId = rule.Id,
                    RuleName = rule.Name,
                    EntityType = entityType,
                    EntityId = currentEntity.Id,
                    TriggerType = rule.TriggerType.ToString(),
                    Status = "Failed",
                    ErrorMessage = ex.Message,
                    ExecutedAt = clock.UtcNow,
                });
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private bool MatchesTrigger<T>(AutomationRule rule, T current, T? previous) where T : TenantEntity
    {
        if (rule.TriggerType == AutomationTriggerType.RecordCreated)
        {
            return true;
        }

        if (rule.TriggerType == AutomationTriggerType.RecordUpdated)
        {
            return true;
        }

        if (rule.TriggerType == AutomationTriggerType.FieldChanged && rule.TriggerConfig is not null && previous is not null)
        {
            var config = JsonSerializer.Deserialize<AutomationTriggerConfig>(rule.TriggerConfig.RootElement.GetRawText());
            if (config is null || string.IsNullOrWhiteSpace(config.FieldKey))
            {
                return true;
            }

            var currentVal = GetFieldValue(current, config.FieldKey);
            var prevVal = GetFieldValue(previous, config.FieldKey);

            if (string.Equals(currentVal, prevVal, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(config.ToValue) &&
                !string.Equals(currentVal, config.ToValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        if (rule.TriggerType == AutomationTriggerType.StageChanged && previous is not null)
        {
            if (current is Deal currDeal && previous is Deal prevDeal)
            {
                if (currDeal.StageId == prevDeal.StageId)
                {
                    return false;
                }

                if (rule.TriggerConfig is not null)
                {
                    var config = JsonSerializer.Deserialize<AutomationTriggerConfig>(rule.TriggerConfig.RootElement.GetRawText());
                    if (config is not null && !string.IsNullOrWhiteSpace(config.ToValue) &&
                        Guid.TryParse(config.ToValue, out var targetStageId) &&
                        currDeal.StageId != targetStageId)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        return true;
    }

    private bool MatchesConditions<T>(AutomationRule rule, string entityType, T entity) where T : TenantEntity
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
            var predicate = FilterCompiler.Compile<T>(group, entityInfo.SystemFields, context).Compile();
            return predicate(entity);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to evaluate condition for rule '{RuleName}'", rule.Name);
            return false;
        }
    }

    private async Task ExecuteActionsAsync<T>(
        AutomationRule rule,
        string entityType,
        T entity,
        CancellationToken cancellationToken
    ) where T : TenantEntity
    {
        var actions = JsonSerializer.Deserialize<List<AutomationActionDefinition>>(rule.Actions.RootElement.GetRawText()) ?? [];

        foreach (var action in actions)
        {
            // If the action has a scheduled delay, queue it via Hangfire and skip immediate execution
            if (action.ScheduledDelayMinutes is > 0 && tenant.TenantId is not null)
            {
                var serialized = JsonSerializer.Serialize(action);
                backgroundJobs.Schedule<AutomationScheduledJob>(
                    job => job.ExecuteScheduledAsync(tenant.TenantId.Value, rule.Id, entityType, entity.Id, serialized, CancellationToken.None),
                    TimeSpan.FromMinutes(action.ScheduledDelayMinutes.Value));

                logger.LogInformation("Scheduled action queued with {Minutes} min delay for rule '{RuleName}' on {EntityType} {Id}",
                    action.ScheduledDelayMinutes, rule.Name, entityType, entity.Id);
                continue;
            }

            switch (action.Type)
            {
                case AutomationActionType.UpdateField:
                    ApplyFieldUpdate(entity, action);
                    break;

                case AutomationActionType.CreateTask:
                    CreateLinkedTask(entityType, entity, action);
                    break;

                case AutomationActionType.SendNotification:
                    await SendInAppNotificationAsync(rule, entityType, entity, action, cancellationToken);
                    break;

                case AutomationActionType.Webhook:
                    await SendWebhookAsync(entityType, entity, action, cancellationToken);
                    break;

                case AutomationActionType.SendEmail:
                    await SendEmailAsync(rule, entityType, entity, action, cancellationToken);
                    break;
            }
        }
    }

    private void ApplyFieldUpdate<T>(T entity, AutomationActionDefinition action) where T : TenantEntity
    {
        if (string.IsNullOrWhiteSpace(action.FieldKey) || action.FieldValue is null)
        {
            return;
        }

        // 1. Try matching C# property directly or by field mapping
        var prop = entity.GetType().GetProperty(action.FieldKey, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
        if (prop is not null && prop.CanWrite)
        {
            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (targetType == typeof(string))
            {
                prop.SetValue(entity, action.FieldValue);
            }
            else if (targetType == typeof(int) && int.TryParse(action.FieldValue, out var i))
            {
                prop.SetValue(entity, i);
            }
            else if (targetType == typeof(decimal) && decimal.TryParse(action.FieldValue, out var d))
            {
                prop.SetValue(entity, d);
            }
            else if (targetType == typeof(bool) && bool.TryParse(action.FieldValue, out var b))
            {
                prop.SetValue(entity, b);
            }
            else if (targetType == typeof(Guid) && Guid.TryParse(action.FieldValue, out var g))
            {
                prop.SetValue(entity, g);
            }
            return;
        }

        // 2. If it's a custom field (cf_*) or entity has custom fields
        if (entity is IHasCustomFields customEntity)
        {
            var dict = customEntity.CustomFields is null
                ? new Dictionary<string, JsonElement>()
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(customEntity.CustomFields.RootElement.GetRawText()) ?? new();

            dict[action.FieldKey] = JsonSerializer.SerializeToElement(action.FieldValue);
            customEntity.CustomFields = JsonDocument.Parse(JsonSerializer.Serialize(dict));
        }
    }

    private void CreateLinkedTask<T>(string entityType, T entity, AutomationActionDefinition action) where T : TenantEntity
    {
        var ownerId = (entity as IOwnedEntity)?.OwnerId ?? Guid.Empty;
        var dueOffset = action.OffsetDays ?? 1;

        var task = new Activity
        {
            Type = "Task",
            Subject = action.Subject ?? $"Follow up on {entityType}",
            Priority = action.Priority ?? "Normal",
            Status = "Open",
            DueAt = clock.UtcNow.AddDays(dueOffset),
            OwnerId = ownerId != Guid.Empty ? ownerId : tenant.UserId ?? Guid.Empty,
            LeadId = entity is Lead l ? l.Id : null,
            ContactId = entity is Contact c ? c.Id : null,
            AccountId = entity is Account a ? a.Id : null,
            DealId = entity is Deal d ? d.Id : null,
        };

        db.Activities.Add(task);
    }

    private async Task SendInAppNotificationAsync<T>(
        AutomationRule rule,
        string entityType,
        T entity,
        AutomationActionDefinition action,
        CancellationToken cancellationToken
    ) where T : TenantEntity
    {
        var ownerId = (entity as IOwnedEntity)?.OwnerId;
        if (ownerId is null || ownerId == Guid.Empty)
        {
            ownerId = tenant.UserId;
        }

        if (ownerId is null || ownerId == Guid.Empty)
        {
            return;
        }

        var message = action.NotificationMessage ?? $"Automation rule '{rule.Name}' executed on {entityType}.";
        await notifications.NotifyAsync(new NotificationRequest(
            UserIds: [ownerId.Value],
            Type: "Automation",
            Title: rule.Name,
            Body: message,
            EntityType: entityType,
            EntityId: entity.Id,
            Url: $"/{entityType}/{entity.Id}",
            IncludeActor: true,
            InAppOnly: true
        ), cancellationToken);
    }

    private async Task SendEmailAsync<T>(
        AutomationRule rule,
        string entityType,
        T entity,
        AutomationActionDefinition action,
        CancellationToken cancellationToken
    ) where T : TenantEntity
    {
        var recipient = action.EmailRecipient
            ?? (entity as Lead)?.Email
            ?? (entity as Contact)?.Email;

        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogInformation("SendEmail action skipped for {EntityType} {Id}: no recipient available.", entityType, entity.Id);
            return;
        }

        var subject = action.Subject ?? $"Update regarding {entityType}";
        var body = action.EmailBody ?? action.NotificationMessage ?? $"Automation rule '{rule.Name}' was triggered.";

        await emailSender.SendAsync(recipient, subject, body, cancellationToken);
        logger.LogInformation("Automation email sent to {Recipient} for {EntityType} {Id}", recipient, entityType, entity.Id);
    }

    private async Task SendWebhookAsync<T>(
        string entityType,
        T entity,
        AutomationActionDefinition action,
        CancellationToken cancellationToken
    ) where T : TenantEntity
    {
        if (string.IsNullOrWhiteSpace(action.WebhookUrl) || httpClientFactory is null)
        {
            return;
        }

        // SSRF protection: validate URL against private, loopback, and cloud metadata IPs
        if (!WebhookSecurity.IsSafeUrl(action.WebhookUrl, out var blockReason))
        {
            logger.LogWarning("Blocked SSRF attempt for webhook URL '{Url}': {Reason}", action.WebhookUrl, blockReason);
            throw new InvalidOperationException($"Webhook blocked: {blockReason}");
        }

        try
        {
            var client = httpClientFactory.CreateClient("AutomationWebhooks");
            client.Timeout = TimeSpan.FromSeconds(5);
            var payload = new
            {
                EntityType = entityType,
                EntityId = entity.Id,
                Timestamp = clock.UtcNow,
                Data = entity
            };
            await client.PostAsJsonAsync(action.WebhookUrl, payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogWarning(ex, "Webhook to {Url} failed for automation", action.WebhookUrl);
            throw;
        }
    }

    private static string? GetFieldValue<T>(T? entity, string fieldKey)
    {
        if (entity is null)
        {
            return null;
        }

        var prop = entity.GetType().GetProperty(fieldKey, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
        if (prop is not null)
        {
            return prop.GetValue(entity)?.ToString();
        }

        if (entity is IHasCustomFields customEntity && customEntity.CustomFields is not null)
        {
            if (customEntity.CustomFields.RootElement.TryGetProperty(fieldKey, out var elem))
            {
                return elem.ToString();
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<AutomationRuleDto>> ListRulesAsync(CancellationToken cancellationToken = default)
    {
        var rules = await db.AutomationRules
            .AsNoTracking()
            .OrderBy(r => r.EntityType)
            .ThenBy(r => r.Priority)
            .ToListAsync(cancellationToken);

        return rules.Select(MapRule).ToList();
    }

    public async Task<AutomationRuleDto> GetRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await db.AutomationRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Automation rule", id);
        return MapRule(rule);
    }

    public async Task<AutomationRuleDto> SaveRuleAsync(Guid? id, SaveAutomationRuleRequest request, CancellationToken cancellationToken = default)
    {
        var rule = id is null
            ? new AutomationRule()
            : await db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException("Automation rule", id);

        rule.Name = request.Name.Trim();
        rule.EntityType = request.EntityType.Trim().ToLowerInvariant();
        rule.TriggerType = request.TriggerType;
        rule.TriggerConfig = request.TriggerConfig is not null
            ? JsonDocument.Parse(JsonSerializer.Serialize(request.TriggerConfig))
            : null;
        rule.Conditions = request.Conditions is not null
            ? JsonDocument.Parse(JsonSerializer.Serialize(request.Conditions))
            : null;
        rule.Actions = JsonDocument.Parse(JsonSerializer.Serialize(request.Actions));
        rule.IsActive = request.IsActive;
        rule.Priority = request.Priority;

        if (id is null)
        {
            db.AutomationRules.Add(rule);
        }

        await db.SaveChangesAsync(cancellationToken);
        return MapRule(rule);
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Automation rule", id);
        db.AutomationRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AutomationRuleDto> ToggleRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("Automation rule", id);
        rule.IsActive = !rule.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return MapRule(rule);
    }

    public async Task<IReadOnlyList<AutomationExecutionLogDto>> ListLogsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var logs = await db.AutomationExecutionLogs
            .AsNoTracking()
            .OrderByDescending(l => l.ExecutedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return logs.Select(l => new AutomationExecutionLogDto(
            l.Id,
            l.RuleId,
            l.RuleName,
            l.EntityType,
            l.EntityId,
            l.TriggerType,
            l.Status,
            l.ErrorMessage,
            l.ExecutedAt
        )).ToList();
    }

    private static AutomationRuleDto MapRule(AutomationRule r)
    {
        var triggerConfig = r.TriggerConfig is not null
            ? JsonSerializer.Deserialize<AutomationTriggerConfig>(r.TriggerConfig.RootElement.GetRawText())
            : null;

        var conditions = r.Conditions is not null
            ? JsonSerializer.Deserialize<FilterGroup>(r.Conditions.RootElement.GetRawText())
            : null;

        var actions = JsonSerializer.Deserialize<List<AutomationActionDefinition>>(r.Actions.RootElement.GetRawText()) ?? [];

        return new AutomationRuleDto(
            r.Id,
            r.Name,
            r.EntityType,
            r.TriggerType,
            triggerConfig,
            conditions,
            actions,
            r.IsActive,
            r.Priority,
            r.CreatedAt
        );
    }
}
