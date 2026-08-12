using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

public class OnlineAsyncAttemptRepository : Repository<OnlineAsyncAttempt>, IOnlineAsyncAttemptRepository
{
    public OnlineAsyncAttemptRepository(ApplicationDbContext db) : base(db)
    {
    }

    public async Task<OnlineAsyncAttempt?> GetByTournamentAndUserAsync(Guid tournamentId, Guid userId, CancellationToken ct = default)
    {
        return await _db.OnlineAsyncAttempts
            .Include(a => a.Tournament)
            .Include(a => a.User)
            .Include(a => a.ReviewedByUser)
            .FirstOrDefaultAsync(a => a.TournamentId == tournamentId && a.UserId == userId, ct);
    }

    public async Task<List<OnlineAsyncAttempt>> GetAttemptsByTournamentAsync(Guid tournamentId, CancellationToken ct = default)
    {
        return await _db.OnlineAsyncAttempts
            .Include(a => a.User)
            .Include(a => a.ReviewedByUser)
            .Where(a => a.TournamentId == tournamentId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<OnlineAsyncAttempt>> GetLeaderboardAsync(Guid tournamentId, CancellationToken ct = default)
    {
        // Ranks approved results first by FinalTimeMs ascending, then DNFs/unapproved
        return await _db.OnlineAsyncAttempts
            .Include(a => a.User)
            .Where(a => a.TournamentId == tournamentId && a.ReviewStatus == "APPROVED")
            .OrderBy(a => a.IsDnf)
            .ThenBy(a => a.FinalTimeMs ?? int.MaxValue)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);
    }
}
