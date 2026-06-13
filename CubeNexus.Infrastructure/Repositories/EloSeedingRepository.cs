using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

/// <summary>
/// Repository cho các query phức tạp trong luồng Elo Seeding (Giai đoạn 1).
/// Tập trung toàn bộ logic truy vấn Practice data và Seeding threshold.
/// </summary>
public class EloSeedingRepository : IEloSeedingRepository
{
    private readonly ApplicationDbContext _db;

    public EloSeedingRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    /// Lấy các lượt giải hợp lệ: không DNF, time > 0.
    /// Join practice_sessions để lọc theo user và puzzle type.
    /// Sắp xếp gần nhất trước để service dễ lấy N lượt gần nhất.
    public async Task<List<PracticeSolve>> GetValidPracticeSolvesAsync(
        Guid userId,
        Guid puzzleTypeId,
        CancellationToken ct = default)
    {
        return await _db.PracticeSolves
            .Include(s => s.Session)
            .Include(s => s.PenaltyType)
            .Where(s => s.Session.UserId == userId
                     && s.Session.PuzzleTypeId == puzzleTypeId
                     && !s.IsDnf
                     && s.TimeMs > 0)
            .OrderByDescending(s => s.SolvedAt)
            .ToListAsync(ct);
    }

    public async Task<List<PracticeSolve>> GetRecentPracticeSolvesAsync(
        Guid userId,
        Guid puzzleTypeId,
        int take,
        CancellationToken ct = default)
    {
        return await _db.PracticeSolves
            .Include(s => s.Session)
            .Include(s => s.PenaltyType)
            .Where(s => s.Session.UserId == userId
                     && s.Session.PuzzleTypeId == puzzleTypeId)
            .OrderByDescending(s => s.SolvedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<PracticeAo5Snapshot?> GetUnusedAo5SnapshotAsync(
        Guid userId,
        Guid puzzleTypeId,
        CancellationToken ct = default)
    {
        return await _db.PracticeAo5Snapshots
            .Where(s => s.UserId == userId
                     && s.PuzzleTypeId == puzzleTypeId
                     && !s.IsUsedForSeeding)
            .OrderByDescending(s => s.CalculatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc/>
    /// Tra ngưỡng theo sort_order tăng dần.
    /// Điều kiện: min_time_ms <= ao5Ms < max_time_ms (NULL = không giới hạn).
    public async Task<EloSeedThreshold?> GetMatchingThresholdAsync(
        Guid puzzleTypeId,
        int ao5Ms,
        CancellationToken ct = default)
    {
        var thresholds = await _db.EloSeedThresholds
            .Where(t => t.PuzzleTypeId == puzzleTypeId)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);

        // Thực hiện filter phía memory vì logic null-check phức tạp hơn SQL expression tree
        return thresholds.FirstOrDefault(t =>
            (t.MinTimeMs == null || ao5Ms >= t.MinTimeMs) &&
            (t.MaxTimeMs == null || ao5Ms < t.MaxTimeMs));
    }

    /// <inheritdoc/>
    public void AddSnapshot(PracticeAo5Snapshot snapshot)
        => _db.PracticeAo5Snapshots.Add(snapshot);
}
