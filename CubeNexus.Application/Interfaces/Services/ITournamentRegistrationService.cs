using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Registration;

namespace CubeNexus.Application.Interfaces.Services;

public interface ITournamentRegistrationService
{
    Task<RegistrationResultDto> RegisterCompetitorAsync(Guid tournamentId, Guid userId, RegisterTournamentDto dto, CancellationToken ct = default);
    Task<RegistrationResultDto> CancelUserRegistrationAsync(Guid registrationId, Guid userId, CancellationToken ct = default);
    Task<List<RegistrationResultDto>> GetUserRegistrationsAsync(Guid userId, CancellationToken ct = default);
    Task<RegistrationResultDto> GetUserRegistrationByIdAsync(Guid registrationId, Guid userId, CancellationToken ct = default);
    Task<RegisteredEventDetailDto> OverrideSeedAsync(Guid registrationEventId, OverrideSeedDto dto, CancellationToken ct = default);
    Task<List<EventCompetitorSeedDto>> GetEventCompetitorsSortedAsync(Guid eventId, CancellationToken ct = default);

    // Manager endpoints
    Task<List<TournamentRegistrationDetailDto>> GetTournamentRegistrationsAsync(Guid tournamentId, CancellationToken ct = default);
    Task<DemoParticipantGenerationResultDto> GenerateDemoParticipantsAsync(Guid tournamentId, Guid managerId, int count = 20, CancellationToken ct = default);
    Task<RegistrationResultDto> UpdateRegistrationStatusAsync(Guid registrationId, string status, CancellationToken ct = default);
    Task<RegistrationResultDto> ManuallyCheckInAsync(Guid registrationId, CancellationToken ct = default);
}
