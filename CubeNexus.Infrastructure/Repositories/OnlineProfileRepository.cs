using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

public class OnlineProfileRepository : Repository<OnlineProfile>, IOnlineProfileRepository
{
    public OnlineProfileRepository(ApplicationDbContext db) : base(db) { }

    public async Task<OnlineProfile?> GetByUserIdAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        return await _db.OnlineProfiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
    }

    public async Task<(List<OnlineProfile> Items, int TotalCount)> GetLeaderboardAsync(
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.OnlineProfiles
            .Include(p => p.User)
            .Where(p => p.IsPlacementCompleteStandard)
            .OrderByDescending(p => p.EloStandard);

        int totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
