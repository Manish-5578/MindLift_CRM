using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IAccountHierarchyService
{
    Task<AccountHierarchyTreeDto> GetHierarchyAsync(Guid accountId, CancellationToken ct = default);
    Task<AccountHierarchyMetricsDto> GetRollupsAsync(Guid accountId, CancellationToken ct = default);
    Task<bool> SetParentAsync(Guid accountId, Guid? parentAccountId, CancellationToken ct = default);
}
