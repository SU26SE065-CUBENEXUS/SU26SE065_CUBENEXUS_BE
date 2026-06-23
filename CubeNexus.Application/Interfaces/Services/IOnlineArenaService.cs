using CubeNexus.Application.DTOs.Arena;

namespace CubeNexus.Application.Interfaces.Services;

public interface IOnlineArenaService
{
    Task<MatchResultDto> RecordMatchResultAsync(
        Guid matchId, Guid? winnerId, CancellationToken ct = default);

    Task<OnlineProfileDto?> GetPlayerProfileAsync(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default);

    Task<LeaderboardResponseDto> GetLeaderboardAsync(
        Guid puzzleTypeId, int page = 1, int pageSize = 50, CancellationToken ct = default);

    Task<PlayerEligibilityDto> GetPlayerEligibilityAsync(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default);
}
