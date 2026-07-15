using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

public interface IRegistrationRepository : IRepository<Registration>
{
    Task<bool> HasUserRegisteredAsync(Guid tournamentId, Guid userId, CancellationToken ct = default);
    Task<List<Registration>> GetUserRegistrationsAsync(Guid userId, CancellationToken ct = default);
    Task<Registration?> GetRegistrationWithEventsAsync(Guid registrationId, Guid userId, CancellationToken ct = default);
    Task<List<Result>> GetLatestOfficialResultsAsync(Guid userId, Guid puzzleTypeId, CancellationToken ct = default);
    Task<Registration?> GetByQrTokenAsync(string qrToken, CancellationToken ct = default);
    Task<Registration?> GetRegistrationWithDetailsAsync(Guid registrationId, CancellationToken ct = default);
    Task<List<Registration>> GetTournamentRegistrationsAsync(Guid tournamentId, CancellationToken ct = default);
}
