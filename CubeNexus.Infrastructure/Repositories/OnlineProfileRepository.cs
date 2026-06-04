using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

/// <summary>
/// Repository cho Online Arena Profile với các query phức tạp.
/// Kế thừa Repository&lt;OnlineProfile&gt; để có sẵn CRUD cơ bản.
/// </summary>
public class OnlineProfileRepository : Repository<OnlineProfile>, IOnlineProfileRepository
{
    public OnlineProfileRepository(ApplicationDbContext db) : base(db) { }

    /// <inheritdoc/>
    public async Task<OnlineProfile?> GetByUserAndPuzzleTypeAsync(
        Guid userId,
        Guid puzzleTypeId,
        CancellationToken ct = default)
    {
        return await _db.OnlineProfiles
            .Include(p => p.User)
            .Include(p => p.PuzzleType)
            .FirstOrDefaultAsync(
                p => p.UserId == userId && p.PuzzleTypeId == puzzleTypeId,
                ct);
    }

    /// <inheritdoc/>
    public async Task<(List<OnlineProfile> Items, int TotalCount)> GetLeaderboardAsync(
        Guid puzzleTypeId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.OnlineProfiles
            .Include(p => p.User)
            .Where(p => p.PuzzleTypeId == puzzleTypeId
                     && p.IsPlacementComplete)   // Chỉ hiển thị players đã placed
            .OrderByDescending(p => p.Elo);      // Elo cao nhất trước

        int totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
