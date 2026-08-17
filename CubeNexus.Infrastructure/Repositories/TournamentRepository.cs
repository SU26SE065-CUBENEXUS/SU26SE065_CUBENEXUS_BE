using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

public class TournamentRepository : Repository<Tournament>, ITournamentRepository
{
    public TournamentRepository(ApplicationDbContext db) : base(db)
    {
    }

    public async Task<List<Tournament>> GetPublicTournamentsAsync(CancellationToken ct = default)
    {
        var publicStatuses = new[] { "REGISTRATION_OPEN", "REGISTRATION_CLOSED", "PUBLISHED", "ONGOING", "COMPLETED" };
        return await _set
            .Include(t => t.CreatedByUser)
            .Include(t => t.Events)
                .ThenInclude(e => e.PuzzleType)
            .Include(t => t.Events)
                .ThenInclude(e => e.MedleyPuzzles)
                    .ThenInclude(mp => mp.PuzzleType)
            .Where(t => publicStatuses.Contains(t.StatusCode))
            .OrderByDescending(t => t.StartDate)
            .ToListAsync(ct);
    }

    public async Task<Tournament?> GetTournamentWithEventsAndPuzzlesAsync(Guid tournamentId, CancellationToken ct = default)
    {
        return await _set
            .Include(t => t.CreatedByUser)
            .Include(t => t.Events)
                .ThenInclude(e => e.PuzzleType)
            .Include(t => t.Events)
                .ThenInclude(e => e.MedleyPuzzles)
                    .ThenInclude(mp => mp.PuzzleType)
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);
    }
}
