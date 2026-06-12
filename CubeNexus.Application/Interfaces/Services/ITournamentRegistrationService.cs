using CubeNexus.Application.DTOs.Registration;

namespace CubeNexus.Application.Interfaces.Services;

public interface ITournamentRegistrationService
{
    Task<RegistrationResultDto> RegisterCompetitorAsync(Guid tournamentId, Guid userId, RegisterTournamentDto dto, CancellationToken ct = default);
    Task<List<RegistrationResultDto>> GetUserRegistrationsAsync(Guid userId, CancellationToken ct = default);
    Task<RegistrationResultDto> GetUserRegistrationByIdAsync(Guid registrationId, Guid userId, CancellationToken ct = default);
    Task<RegisteredEventDetailDto> OverrideSeedAsync(Guid registrationEventId, OverrideSeedDto dto, CancellationToken ct = default);
    Task<List<EventCompetitorSeedDto>> GetEventCompetitorsSortedAsync(Guid eventId, CancellationToken ct = default);
}
