using MindLift.Application.Common.Time;

namespace MindLift.Application.Abstractions;

public interface ISalesMetrics
{
    Task<decimal> WonAmountAsync(IReadOnlyList<Guid>? userIds, DateRange range, Guid? pipelineId, CancellationToken ct);
    Task<int> WonCountAsync(IReadOnlyList<Guid>? userIds, DateRange range, Guid? pipelineId, CancellationToken ct);
    Task<int> LostCountAsync(IReadOnlyList<Guid>? userIds, DateRange range, Guid? pipelineId, CancellationToken ct);
    Task<(decimal Total, decimal Weighted, int Count)> OpenPipelineAsync(IReadOnlyList<Guid>? userIds, Guid? pipelineId, CancellationToken ct);
    Task<int> NewLeadsAsync(IReadOnlyList<Guid>? userIds, DateRange range, CancellationToken ct);
    Task<int> ConvertedLeadsCohortAsync(IReadOnlyList<Guid>? userIds, DateRange createdRange, CancellationToken ct);
    Task<int> ActivitiesCompletedAsync(IReadOnlyList<Guid>? userIds, DateRange range, string? type, CancellationToken ct);
    Task<int> OverdueTasksAsync(IReadOnlyList<Guid>? userIds, CancellationToken ct);
}
