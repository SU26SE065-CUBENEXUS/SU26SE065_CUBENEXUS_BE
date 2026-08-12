using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

public interface IOnlineAsyncAttemptRepository : IRepository<OnlineAsyncAttempt>
{
    Task<OnlineAsyncAttempt?> GetByTournamentAndUserAsync(Guid tournamentId, Guid userId, CancellationToken ct = default);
    Task<List<OnlineAsyncAttempt>> GetAttemptsByTournamentAsync(Guid tournamentId, CancellationToken ct = default);
    Task<List<OnlineAsyncAttempt>> GetLeaderboardAsync(Guid tournamentId, CancellationToken ct = default);
}
