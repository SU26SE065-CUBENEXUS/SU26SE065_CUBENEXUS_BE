using CubeNexus.Application.DTOs;

namespace CubeNexus.Application.Interfaces.Services;

public interface IOnlineAsyncTournamentService
{
    Task<OnlineAsyncTournamentDto> CreateTournamentAsync(Guid managerUserId, CreateOnlineAsyncTournamentRequest request, CancellationToken ct = default);
    Task<OnlineAsyncTournamentDto> GetTournamentByIdAsync(Guid tournamentId, Guid? userId = null, CancellationToken ct = default);
    Task<List<OnlineAsyncTournamentDto>> ListTournamentsAsync(string? status = null, Guid? userId = null, CancellationToken ct = default);
    Task<bool> RegisterCompetitorAsync(Guid tournamentId, Guid userId, CancellationToken ct = default);
    
    Task<StartOnlineAsyncAttemptResponse> StartAttemptAsync(Guid tournamentId, Guid userId, CancellationToken ct = default);
    Task<OnlineAsyncAttemptStateDto> GetAttemptStateAsync(Guid attemptId, Guid userId, CancellationToken ct = default);
    Task<VerifyAsyncScrambleResponse> VerifyScrambleAsync(Guid attemptId, Guid userId, VerifyAsyncScrambleRequest request, CancellationToken ct = default);
    Task<StartAsyncSolveTimerResponse> StartSolveTimerAsync(Guid attemptId, Guid userId, StartAsyncSolveTimerRequest request, CancellationToken ct = default);
    Task<FinishAsyncSolveTimerResponse> FinishSolveTimerAsync(Guid attemptId, Guid userId, FinishAsyncSolveTimerRequest request, CancellationToken ct = default);
    Task<FinishAsyncSolveTimerResponse> VerifyFinishAsync(Guid attemptId, Guid userId, VerifyAsyncFinishRequest request, CancellationToken ct = default);
    Task<AsyncAttemptVideoUploadResponse> UploadVideoEvidenceAsync(Guid attemptId, Guid userId, Stream content, string contentType, CancellationToken ct = default);
    Task<string> GetVideoPlaybackUrlAsync(Guid attemptId, Guid reviewerUserId, CancellationToken ct = default);
    
    Task<List<AsyncLeaderboardEntryDto>> GetAttemptsForReviewAsync(Guid tournamentId, Guid reviewerUserId, CancellationToken ct = default);
    Task<AsyncLeaderboardEntryDto> ReviewAttemptAsync(Guid attemptId, Guid reviewerUserId, ReviewAsyncAttemptRequest request, CancellationToken ct = default);
    Task<List<AsyncLeaderboardEntryDto>> GetLeaderboardAsync(Guid tournamentId, CancellationToken ct = default);
}
