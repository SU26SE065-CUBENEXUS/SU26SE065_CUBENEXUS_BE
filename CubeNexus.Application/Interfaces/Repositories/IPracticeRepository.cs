using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

public interface IPracticeRepository
{
    // ── Session ──────────────────────────────────────────────────

    Task<PracticeSession?> GetSessionByIdAsync(Guid sessionId);

    /// <summary>Lấy session kèm tất cả solves, sắp xếp theo SolvedAt asc</summary>
    Task<PracticeSession?> GetSessionWithSolvesAsync(Guid sessionId);

    Task AddSessionAsync(PracticeSession session);

    Task<PracticeSession?> GetActiveSessionAsync(Guid userId, Guid puzzleTypeId);

    // ── Attempt ──────────────────────────────────────────────────

    Task AddAttemptAsync(PracticeAttempt attempt);

    Task<PracticeAttempt?> GetAttemptByIdAsync(Guid attemptId);

    Task<PracticeAttempt?> GetActiveAttemptAsync(Guid sessionId);

    // ── Solve ────────────────────────────────────────────────────

    Task AddSolveAsync(PracticeSolve solve);

    /// <summary>Lấy N lần giải gần nhất của session (sắp xếp theo SolvedAt desc)</summary>
    Task<IReadOnlyList<PracticeSolve>> GetLatestSolvesAsync(Guid sessionId, int take);

    /// <summary>Lấy toàn bộ solves của session (sắp xếp SolvedAt asc)</summary>
    Task<IReadOnlyList<PracticeSolve>> GetAllSolvesAsync(Guid sessionId);

    Task<PenaltyType?> GetPenaltyTypeByCodeAsync(string code);

    // ── History ──────────────────────────────────────────────────

    /// <summary>Lấy danh sách session của user, có phân trang</summary>
    Task<IReadOnlyList<PracticeSession>> GetSessionsByUserAsync(
        Guid userId, Guid? puzzleTypeId, int skip, int take);

    /// <summary>Lấy N lượt giải practice gần nhất của user theo puzzle type.</summary>
    Task<IReadOnlyList<PracticeSolve>> GetRecentSolvesForUserAsync(
        Guid userId, Guid puzzleTypeId, int take, CancellationToken ct = default);

    /// <summary>Đếm số solve trong session</summary>
    Task<int> CountSolvesAsync(Guid sessionId);
}
