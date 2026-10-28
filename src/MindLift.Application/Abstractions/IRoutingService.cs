using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IRoutingService
{
    // Territory Management
    Task<TerritoryDto> CreateTerritoryAsync(SaveTerritoryRequest req, CancellationToken ct = default);
    Task<TerritoryDto> GetTerritoryByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TerritoryDto>> ListTerritoriesAsync(bool? activeOnly = null, CancellationToken ct = default);
    Task<TerritoryDto> UpdateTerritoryAsync(Guid id, SaveTerritoryRequest req, CancellationToken ct = default);
    Task<bool> DeleteTerritoryAsync(Guid id, CancellationToken ct = default);

    // Member Management
    Task<TerritoryMemberDto> AddOrUpdateMemberAsync(Guid territoryId, SaveTerritoryMemberRequest req, CancellationToken ct = default);
    Task<bool> RemoveMemberAsync(Guid territoryId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<TerritoryMemberDto>> ListMembersAsync(Guid territoryId, CancellationToken ct = default);

    // Lead Routing & Simulation
    Task<RouteLeadResultDto> RouteLeadAsync(Guid leadId, RouteLeadRequest? req = null, CancellationToken ct = default);
    Task<SimulateRoutingResultDto> SimulateRoutingAsync(SimulateRoutingRequest req, CancellationToken ct = default);
    Task<IReadOnlyList<LeadRoutingLogDto>> ListRoutingLogsAsync(Guid? leadId = null, Guid? territoryId = null, int limit = 50, CancellationToken ct = default);
}
