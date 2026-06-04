using CubeNexus.Application.DTOs.Arena;

namespace CubeNexus.Application.Interfaces.Services;

/// <summary>
/// Dịch vụ xử lý Giai đoạn 2 &amp; 3: Placement Phase và Elo ổn định.
/// </summary>
public interface IOnlineArenaService
{
    Task<MatchResultDto> RecordMatchResultAsync(
        Guid matchId, Guid? winnerId, CancellationToken ct = default);

    Task<OnlineProfileDto?> GetPlayerProfileAsync(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default);

    Task<LeaderboardResponseDto> GetLeaderboardAsync(
        Guid puzzleTypeId, int page = 1, int pageSize = 50, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra tư cách tham gia PVP của người chơi.
    /// Trả về CanJoinPvp + lý do bị chặn (nếu có) + trạng thái giai đoạn hiện tại.
    /// </summary>
    Task<PlayerEligibilityDto> GetPlayerEligibilityAsync(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default);
}

