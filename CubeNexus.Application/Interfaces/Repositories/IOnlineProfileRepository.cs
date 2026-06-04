using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

/// <summary>
/// Repository cho các query phức tạp liên quan đến Online Arena Profile.
/// Kế thừa IRepository để có đủ CRUD cơ bản.
/// </summary>
public interface IOnlineProfileRepository : IRepository<OnlineProfile>
{
    /// <summary>
    /// Lấy profile của user theo puzzle type, kèm navigation properties cần thiết.
    /// </summary>
    Task<OnlineProfile?> GetByUserAndPuzzleTypeAsync(
        Guid userId,
        Guid puzzleTypeId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy bảng xếp hạng Global Top Rank.
    /// Chỉ bao gồm players đã hoàn thành Placement (is_placement_complete = true).
    /// Kèm navigation User để hiển thị tên và avatar.
    /// Sắp xếp theo Elo giảm dần, hỗ trợ phân trang.
    /// </summary>
    Task<(List<OnlineProfile> Items, int TotalCount)> GetLeaderboardAsync(
        Guid puzzleTypeId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
