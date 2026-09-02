using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

public interface ITournamentRepository : IRepository<Tournament>
{
    Task<List<Tournament>> GetPublicTournamentsAsync(CancellationToken ct = default);
    Task<Tournament?> GetTournamentWithEventsAndPuzzlesAsync(Guid tournamentId, CancellationToken ct = default);
    Task<int> OpenDueRegistrationsAsync(DateTime nowUtc, CancellationToken ct = default);
}
