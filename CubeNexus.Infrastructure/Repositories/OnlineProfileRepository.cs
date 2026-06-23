using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

/// <summary>
/// Repository cho Online Arena Profile với các query phức tạp.
/// Kế thừa Repository<OnlineProfile> để có sẵn CRUD cơ bản.
/// </summary>
public class OnlineProfileRepository : Repository<OnlineProfile>, 
    CubeNexus.Application.Interfaces.Repositories.IOnlineProfileRepository,
    CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository
{
    public OnlineProfileRepository(ApplicationDbContext db) : base(db) { }

    // --- CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository ---
    public Task<OnlineProfile?> GetProfileAsync(Guid userId, Guid puzzleTypeId) => 
        _db.Set<OnlineProfile>().FirstOrDefaultAsync(p => p.UserId == userId && p.PuzzleTypeId == puzzleTypeId);

    public Task<List<OnlineProfile>> GetUserProfilesAsync(Guid userId) =>
        _db.Set<OnlineProfile>().Where(p => p.UserId == userId).ToListAsync();

    public Task<List<OnlineProfile>> GetLeaderboardAsync(Guid puzzleTypeId, int top = 100) =>
        _db.Set<OnlineProfile>().Include(p => p.User).Where(p => p.PuzzleTypeId == puzzleTypeId && p.IsPlacementComplete)
                .OrderByDescending(p => p.Elo).Take(top).ToListAsync();

    public Task AddAsync(OnlineProfile profile) => _db.Set<OnlineProfile>().AddAsync(profile).AsTask();

    // Custom update is not needed to hide base, but we can call base Update or use generic Update
    // Let's implement it explicitly if needed or reuse base Update. 
    // The interface requires void Update(OnlineProfile profile) which matches base Repository<OnlineProfile>.Update
    // Let's make sure it is explicitly mapped if compiler complains:
    void CubeNexus.Application.Interfaces.OnlineArena.IOnlineProfileRepository.Update(OnlineProfile profile) => base.Update(profile);

    // --- CubeNexus.Application.Interfaces.Repositories.IOnlineProfileRepository ---
    public async Task<OnlineProfile?> GetByUserIdAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        return await _db.OnlineProfiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
    }

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
