using CubeNexus.Application.DTOs.Tournament;

namespace CubeNexus.Application.Interfaces.Services;

public interface ITournamentService
{
    Task<TournamentDetailDto> CreateTournamentAsync(CreateTournamentDto dto, Guid managerId, CancellationToken ct = default);
    Task<List<TournamentDetailDto>> GetPublicTournamentsAsync(CancellationToken ct = default);
    Task<TournamentDetailDto> GetTournamentByIdAsync(Guid id, CancellationToken ct = default);
    Task<TournamentDetailDto> CloseRegistrationAsync(Guid tournamentId, Guid managerId, CancellationToken ct = default);

    // Tournament-scoped Judge CRUD
    Task<List<TournamentJudgeDto>> GetTournamentJudgesAsync(Guid tournamentId, CancellationToken ct = default);
    Task<TournamentJudgeDto> CreateTournamentJudgeAsync(Guid tournamentId, CreateTournamentJudgeDto dto, Guid managerId, CancellationToken ct = default);
    Task<List<TournamentJudgeDto>> BatchCreateTournamentJudgesAsync(Guid tournamentId, BatchCreateTournamentJudgeDto dto, Guid managerId, CancellationToken ct = default);
    Task<TournamentJudgeDto> UpdateTournamentJudgeAsync(Guid tournamentId, Guid judgeUserId, UpdateTournamentJudgeDto dto, Guid managerId, CancellationToken ct = default);
    Task<TournamentJudgeDto> ResetTournamentJudgePasswordAsync(Guid tournamentId, Guid judgeUserId, ResetJudgePasswordDto dto, Guid managerId, CancellationToken ct = default);
    Task<List<TournamentJudgeDto>> ShuffleTournamentJudgesAsync(Guid tournamentId, ShuffleTournamentJudgesDto dto, Guid managerId, CancellationToken ct = default);
    Task DeleteTournamentJudgeAsync(Guid tournamentId, Guid judgeUserId, Guid managerId, CancellationToken ct = default);
}
