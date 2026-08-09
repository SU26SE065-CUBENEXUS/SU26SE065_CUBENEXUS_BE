using CubeNexus.Application.DTOs.Admin;

namespace CubeNexus.Application.Interfaces.Services;

public interface IAdminTournamentService
{
    Task<AdminTournamentPagedResultDto> GetTournamentsAsync(int page, int pageSize, string? search, string? status, CancellationToken ct = default);
    Task<AdminTournamentDto> GetTournamentByIdAsync(Guid id, CancellationToken ct = default);
    Task<AdminTournamentDto> UpdateTournamentStatusAsync(Guid id, string statusCode, CancellationToken ct = default);
}
