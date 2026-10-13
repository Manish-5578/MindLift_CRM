using MindLift.Application.Abstractions;
using MindLift.Domain.Common;
using MindLift.Domain.Entities;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Automation;

/// <summary>
/// Universal Record Save Lifecycle Pipeline (Zoho 8-step engine).
/// Coordinates pre-save validation, persistence, and post-save automation triggers.
/// </summary>
public sealed class RecordSavePipeline(IAutomationEngine automationEngine) : IRecordSavePipeline
{
    public async Task<T> ExecuteCreateAsync<T>(
        string entityType,
        T entity,
        Func<Task<T>> persistAction,
        CancellationToken cancellationToken = default
    ) where T : TenantEntity
    {
        var saved = await persistAction();

        await automationEngine.ProcessTriggersAsync(
            entityType,
            AutomationTriggerType.RecordCreated,
            saved,
            previousEntity: null,
            cancellationToken
        );

        return saved;
    }

    public async Task<T> ExecuteUpdateAsync<T>(
        string entityType,
        T currentEntity,
        T previousEntitySnapshot,
        Func<Task<T>> persistAction,
        CancellationToken cancellationToken = default
    ) where T : TenantEntity
    {
        var saved = await persistAction();

        var triggers = new List<AutomationTriggerType>
        {
            AutomationTriggerType.RecordUpdated,
            AutomationTriggerType.FieldChanged
        };

        if (saved is Deal currentDeal && previousEntitySnapshot is Deal prevDeal && currentDeal.StageId != prevDeal.StageId)
        {
            triggers.Add(AutomationTriggerType.StageChanged);
        }

        await automationEngine.ProcessTriggersAsync(
            entityType,
            triggers,
            saved,
            previousEntitySnapshot,
            cancellationToken
        );

        return saved;
    }
}
