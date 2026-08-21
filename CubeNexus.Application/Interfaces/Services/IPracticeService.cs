using CubeNexus.Application.DTOs.Practice;

namespace CubeNexus.Application.Interfaces.Services;

public interface IPracticeService
{
    Task<PracticeSessionResponseDto> StartSessionAsync(Guid userId, StartPracticeSessionDto dto);

    [Obsolete("Use WCA attempt flow instead.")]
    Task<PracticeSolveResponseDto> SubmitSolveAsync(Guid userId, SubmitSolveDto dto);

    Task<PracticeSessionSummaryDto> EndSessionAsync(Guid userId, Guid sessionId);

    Task<IReadOnlyList<PracticeSessionResponseDto>> GetMySessionsAsync(
        Guid userId, Guid? puzzleTypeId = null, int page = 1, int pageSize = 20);

    Task<PracticeSessionSummaryDto> GetSessionDetailAsync(Guid userId, Guid sessionId);

    // ── WCA Stackmat attempt flow ───────────────────────────────────────────

    Task<PracticeAttemptResponseDto> CreateAttemptAsync(Guid userId, Guid sessionId);

    Task<PracticeAttemptResponseDto?> GetCurrentAttemptAsync(Guid userId, Guid sessionId);

    Task<PracticeAttemptResponseDto> GetAttemptAsync(Guid userId, Guid attemptId);

    Task<PracticeAttemptResponseDto> HandsOnAsync(Guid userId, Guid attemptId);

    Task<PracticeAttemptResponseDto> ReadyAsync(Guid userId, Guid attemptId);

    Task<PracticeAttemptResponseDto> HandsOffAsync(Guid userId, Guid attemptId);

    Task<PracticeAttemptResponseDto> FinalizeAttemptAsync(
        Guid userId, Guid attemptId, FinalizeAttemptDto dto);

    Task<PracticeAttemptResponseDto> AbortAttemptAsync(
        Guid userId, Guid attemptId, AbortAttemptDto? dto);

    Task ConnectSessionAsync(Guid userId, Guid sessionId);
}
