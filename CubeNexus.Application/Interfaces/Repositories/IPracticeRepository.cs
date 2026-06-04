using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

public interface IPracticeRepository
{
    // ── Session ──────────────────────────────────────────────────

    Task<PracticeSession?> GetSessionByIdAsync(Guid sessionId);

    /// <summary>Lấy session kèm tất cả solves, sắp xếp theo SolvedAt asc</summary>
    Task<PracticeSession?> GetSessionWithSolvesAsync(Guid sessionId);

    Task AddSessionAsync(PracticeSession session);

    // ── Solve ────────────────────────────────────────────────────

    Task AddSolveAsync(PracticeSolve solve);

    /// <summary>Lấy N lần giải gần nhất của session (sắp xếp theo SolvedAt desc)</summary>
    Task<IReadOnlyList<PracticeSolve>> GetLatestSolvesAsync(Guid sessionId, int take);

    /// <summary>Lấy toàn bộ solves của session (sắp xếp SolvedAt asc)</summary>
    Task<IReadOnlyList<PracticeSolve>> GetAllSolvesAsync(Guid sessionId);

    // ── History ──────────────────────────────────────────────────

    /// <summary>Lấy danh sách session của user, có phân trang</summary>
    Task<IReadOnlyList<PracticeSession>> GetSessionsByUserAsync(
        Guid userId, Guid? puzzleTypeId, int skip, int take);

    /// <summary>Đếm số solve trong session</summary>
    Task<int> CountSolvesAsync(Guid sessionId);
}
