using CubeNexus.Application.DTOs.Tournament;

namespace CubeNexus.Application.Interfaces.Services;

public interface ITournamentService
{
    Task<TournamentDetailDto> CreateTournamentAsync(CreateTournamentDto dto, Guid managerId, CancellationToken ct = default);
    Task<List<TournamentDetailDto>> GetPublicTournamentsAsync(CancellationToken ct = default);
    Task<TournamentDetailDto> GetTournamentByIdAsync(Guid id, CancellationToken ct = default);
}
