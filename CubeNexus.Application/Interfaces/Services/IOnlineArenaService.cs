using CubeNexus.Application.DTOs.Arena;

namespace CubeNexus.Application.Interfaces.Services;

public interface IOnlineArenaService
{
    Task<MatchResultDto> RecordMatchResultAsync(
        Guid matchId, Guid? winnerId, CancellationToken ct = default);

    Task<OnlineProfileDto?> GetPlayerProfileAsync(
        Guid userId, CancellationToken ct = default);

    Task<LeaderboardResponseDto> GetLeaderboardAsync(
        int page = 1, int pageSize = 50, CancellationToken ct = default);

    Task<PlayerEligibilityDto> GetPlayerEligibilityAsync(
        Guid userId, CancellationToken ct = default);
}
