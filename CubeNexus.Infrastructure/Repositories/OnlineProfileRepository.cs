using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

/// <summary>
/// Repository cho Online Arena Profile với các query phức tạp.
/// Kế thừa Repository&lt;OnlineProfile&gt; để có sẵn CRUD cơ bản.
/// </summary>
public class OnlineProfileRepository : Repository<OnlineProfile>,
    CubeNexus.Application.Interfaces.Repositories.IOnlineProfileRepository,
    CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository
{
    public OnlineProfileRepository(ApplicationDbContext db) : base(db) { }

    // --- CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository ---
    public Task<OnlineProfile?> GetProfileAsync(Guid userId, Guid puzzleTypeId) =>
        GetByUserIdAsync(userId);

    public async Task<List<OnlineProfile>> GetUserProfilesAsync(Guid userId)
    {
        var profile = await GetByUserIdAsync(userId);
        return profile is null ? [] : [profile];
    }

    public async Task<List<OnlineProfile>> GetLeaderboardAsync(Guid puzzleTypeId, int top = 100)
    {
        return await _db.OnlineProfiles
            .Include(p => p.User)
            .Where(p => p.IsPlacementCompleteStandard)
            .OrderByDescending(p => p.EloStandard)
            .Take(top)
            .ToListAsync();
    }

    public Task AddAsync(OnlineProfile profile) => _db.Set<OnlineProfile>().AddAsync(profile).AsTask();

    void CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository.Update(OnlineProfile profile) =>
        base.Update(profile);

    // --- CubeNexus.Application.Interfaces.Repositories.IOnlineProfileRepository ---
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
