using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

/// <summary>
/// Repository cho các query phức tạp trong luồng Elo Seeding (Giai đoạn 1).
/// </summary>
public interface IEloSeedingRepository
{
    /// <summary>
    /// Lấy các lượt giải Practice hợp lệ của user (không DNF, có thời gian > 0).
    /// Join qua practice_sessions để lọc theo user + puzzle type.
    /// Sắp xếp theo thời gian giải gần nhất.
    /// </summary>
    Task<List<PracticeSolve>> GetValidPracticeSolvesAsync(
        Guid userId,
        Guid puzzleTypeId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy snapshot Ao5 chưa dùng (is_used_for_seeding = false), mới nhất.
    /// Dùng khi khởi tạo Online Profile.
    /// </summary>
    Task<PracticeAo5Snapshot?> GetUnusedAo5SnapshotAsync(
        Guid userId,
        Guid puzzleTypeId,
        CancellationToken ct = default);

    /// <summary>
    /// Tra bảng elo_seed_thresholds để tìm ngưỡng phù hợp với Ao5 đã tính.
    /// Điều kiện: min_time_ms &lt;= ao5Ms &lt; max_time_ms (NULL = không giới hạn).
    /// Ưu tiên theo sort_order tăng dần.
    /// </summary>
    Task<EloSeedThreshold?> GetMatchingThresholdAsync(
        Guid puzzleTypeId,
        int ao5Ms,
        CancellationToken ct = default);

    // CRUD cho PracticeAo5Snapshot
    void AddSnapshot(PracticeAo5Snapshot snapshot);
}
