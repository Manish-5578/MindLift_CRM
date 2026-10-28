using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ILeaderboardService
{
    Task<LeaderboardResponseDto> GetLeaderboardAsync(LeaderboardQueryRequest request, CancellationToken ct = default);
    Task<LeaderboardMetaDto> GetMetaAsync(CancellationToken ct = default);
}
