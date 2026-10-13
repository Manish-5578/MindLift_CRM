using MindLift.Domain.Common;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Application.Abstractions;

public interface IAutomationEngine
{
    /// <summary>
    /// Processes active automation rules matching (entityType, triggerType) for the given entity.
    /// Supports loop protection up to depth 3.
    /// </summary>
    Task ProcessTriggersAsync<T>(
        string entityType,
        AutomationTriggerType triggerType,
        T currentEntity,
        T? previousEntity = null,
        CancellationToken cancellationToken = default
    ) where T : TenantEntity;

    /// <summary>
    /// Processes active automation rules matching any of the specified trigger types in a single pass.
    /// Saves all changes to the database in a single transaction.
    /// </summary>
    Task ProcessTriggersAsync<T>(
        string entityType,
        IReadOnlyList<AutomationTriggerType> triggerTypes,
        T currentEntity,
        T? previousEntity = null,
        CancellationToken cancellationToken = default
    ) where T : TenantEntity;

    /// <summary>Lists automation rules for the tenant.</summary>
    Task<IReadOnlyList<AutomationRuleDto>> ListRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a specific automation rule.</summary>
    Task<AutomationRuleDto> GetRuleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates an automation rule.</summary>
    Task<AutomationRuleDto> SaveRuleAsync(Guid? id, SaveAutomationRuleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes an automation rule.</summary>
    Task DeleteRuleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Toggles active status of an automation rule.</summary>
    Task<AutomationRuleDto> ToggleRuleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists recent execution logs for diagnostics.</summary>
    Task<IReadOnlyList<AutomationExecutionLogDto>> ListLogsAsync(int limit = 50, CancellationToken cancellationToken = default);
}
