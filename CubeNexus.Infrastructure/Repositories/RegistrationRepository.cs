using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

public class RegistrationRepository : Repository<Registration>, IRegistrationRepository
{
    public RegistrationRepository(ApplicationDbContext db) : base(db)
    {
    }

    public async Task<bool> HasUserRegisteredAsync(Guid tournamentId, Guid userId, CancellationToken ct = default)
    {
        return await _set.AnyAsync(r => r.TournamentId == tournamentId && r.UserId == userId && r.StatusCode != "CANCELLED", ct);
    }

    public async Task<List<Registration>> GetUserRegistrationsAsync(Guid userId, CancellationToken ct = default)
    {
        return await _set
            .Include(r => r.Tournament)
            .Include(r => r.OfflineRegistrationEvents)
                .ThenInclude(ore => ore.Event)
                    .ThenInclude(e => e.PuzzleType)
            .Include(r => r.OfflineRegistrationEvents)
                .ThenInclude(ore => ore.Event)
                    .ThenInclude(e => e.MedleyPuzzles)
                        .ThenInclude(mp => mp.PuzzleType)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.RegisteredAt)
            .ToListAsync(ct);
    }

    public async Task<Registration?> GetRegistrationWithEventsAsync(Guid registrationId, Guid userId, CancellationToken ct = default)
    {
        return await _set
            .Include(r => r.Tournament)
            .Include(r => r.OfflineRegistrationEvents)
                .ThenInclude(ore => ore.Event)
                    .ThenInclude(e => e.PuzzleType)
            .Include(r => r.OfflineRegistrationEvents)
                .ThenInclude(ore => ore.Event)
                    .ThenInclude(e => e.MedleyPuzzles)
                        .ThenInclude(mp => mp.PuzzleType)
            .FirstOrDefaultAsync(r => r.Id == registrationId && r.UserId == userId, ct);
    }

    public async Task<List<Result>> GetLatestOfficialResultsAsync(Guid userId, Guid puzzleTypeId, CancellationToken ct = default)
    {
        var latestGroupCompetitor = await _db.GroupCompetitors
            .Include(gc => gc.Group)
            .Where(gc => gc.OfflineRegistrationEvent.Registration.UserId == userId &&
                         gc.OfflineRegistrationEvent.Event.PuzzleTypeId == puzzleTypeId &&
                         _db.Results.Any(r => r.GroupCompetitorId == gc.Id))
            .OrderByDescending(gc => gc.Group.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latestGroupCompetitor == null)
        {
            return new List<Result>();
        }

        return await _db.Results
            .Where(r => r.GroupCompetitorId == latestGroupCompetitor.Id)
            .OrderBy(r => r.SolveNumber)
            .ToListAsync(ct);
    }

    public async Task<Registration?> GetByQrTokenAsync(string qrToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(qrToken)) return null;

        return await _set
            .Include(r => r.Tournament)
            .Include(r => r.User)
            .Include(r => r.OfflineRegistrationEvents)
                .ThenInclude(ore => ore.Event)
                    .ThenInclude(e => e.PuzzleType)
            .Include(r => r.OfflineRegistrationEvents)
                .ThenInclude(ore => ore.Event)
                    .ThenInclude(e => e.MedleyPuzzles)
                        .ThenInclude(mp => mp.PuzzleType)
            .FirstOrDefaultAsync(r => r.QrToken == qrToken || r.QrToken.Contains(qrToken), ct);
    }

    public async Task<Registration?> GetRegistrationWithDetailsAsync(Guid registrationId, CancellationToken ct = default)
    {
        return await _set
            .Include(r => r.Tournament)
            .Include(r => r.User)
            .Include(r => r.OfflineRegistrationEvents)
                .ThenInclude(ore => ore.Event)
                    .ThenInclude(e => e.PuzzleType)
            .Include(r => r.OfflineRegistrationEvents)
                .ThenInclude(ore => ore.Event)
                    .ThenInclude(e => e.MedleyPuzzles)
                        .ThenInclude(mp => mp.PuzzleType)
            .FirstOrDefaultAsync(r => r.Id == registrationId, ct);
    }

    public async Task<List<Registration>> GetTournamentRegistrationsAsync(Guid tournamentId, CancellationToken ct = default)
    {
        return await _set
            .Include(r => r.Tournament)
            .Include(r => r.User)
            .Include(r => r.OfflineRegistrationEvents)
                .ThenInclude(ore => ore.Event)
                    .ThenInclude(e => e.PuzzleType)
            .Include(r => r.OfflineRegistrationEvents)
                .ThenInclude(ore => ore.Event)
                    .ThenInclude(e => e.MedleyPuzzles)
                        .ThenInclude(mp => mp.PuzzleType)
            .Where(r => r.TournamentId == tournamentId)
            .OrderByDescending(r => r.RegisteredAt)
            .ToListAsync(ct);
    }
}
