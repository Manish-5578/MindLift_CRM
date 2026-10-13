using MindLift.Domain.Common;
using MindLift.Shared.Enums;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Universal Record Save Lifecycle Pipeline (Zoho 8-step engine).
/// Coordinates validation, assignment, persistence, and post-save automation triggers.
/// </summary>
public interface IRecordSavePipeline
{
    /// <summary>
    /// Executes the full save lifecycle for a created record:
    /// Pre-save assignments -> Persistence -> Post-save Automations (Trigger: RecordCreated).
    /// </summary>
    Task<T> ExecuteCreateAsync<T>(
        string entityType,
        T entity,
        Func<Task<T>> persistAction,
        CancellationToken cancellationToken = default
    ) where T : TenantEntity;

    /// <summary>
    /// Executes the full save lifecycle for an updated record:
    /// Pre-save checks -> Persistence -> Post-save Automations (Trigger: RecordUpdated / FieldChanged).
    /// </summary>
    Task<T> ExecuteUpdateAsync<T>(
        string entityType,
        T currentEntity,
        T previousEntitySnapshot,
        Func<Task<T>> persistAction,
        CancellationToken cancellationToken = default
    ) where T : TenantEntity;
}
