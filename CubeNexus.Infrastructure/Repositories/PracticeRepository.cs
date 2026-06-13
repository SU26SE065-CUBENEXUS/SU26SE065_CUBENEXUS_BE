using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

public class PracticeRepository : IPracticeRepository
{
    private static readonly PracticeAttemptState[] ActiveAttemptStates =
    [
        PracticeAttemptState.Scrambled,
        PracticeAttemptState.HoldingHands,
        PracticeAttemptState.Ready,
        PracticeAttemptState.Solving,
        PracticeAttemptState.Stopped
    ];

    private readonly ApplicationDbContext _db;

    public PracticeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    // ── Session ──────────────────────────────────────────────────────────────

    public async Task<PracticeSession?> GetSessionByIdAsync(Guid sessionId)
        => await _db.PracticeSessions
            .Include(s => s.PuzzleType)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

    public async Task<PracticeSession?> GetSessionWithSolvesAsync(Guid sessionId)
        => await _db.PracticeSessions
            .Include(s => s.PuzzleType)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

    public async Task AddSessionAsync(PracticeSession session)
        => await _db.PracticeSessions.AddAsync(session);

    public async Task<PracticeSession?> GetActiveSessionAsync(Guid userId, Guid puzzleTypeId)
        => await _db.PracticeSessions
            .Where(s => s.UserId == userId
                     && s.PuzzleTypeId == puzzleTypeId
                     && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

    // ── Attempt ──────────────────────────────────────────────────────────────

    public async Task AddAttemptAsync(PracticeAttempt attempt)
        => await _db.PracticeAttempts.AddAsync(attempt);

    public async Task<PracticeAttempt?> GetAttemptByIdAsync(Guid attemptId)
        => await _db.PracticeAttempts
            .Include(a => a.PenaltyType)
            .Include(a => a.Solve)
            .FirstOrDefaultAsync(a => a.Id == attemptId);

    public async Task<PracticeAttempt?> GetActiveAttemptAsync(Guid sessionId)
        => await _db.PracticeAttempts
            .Include(a => a.PenaltyType)
            .Include(a => a.Solve)
            .Where(a => a.SessionId == sessionId && ActiveAttemptStates.Contains(a.State))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

    // ── Solve ─────────────────────────────────────────────────────────────────

    public async Task AddSolveAsync(PracticeSolve solve)
        => await _db.PracticeSolves.AddAsync(solve);

    public async Task<IReadOnlyList<PracticeSolve>> GetLatestSolvesAsync(Guid sessionId, int take)
        => await _db.PracticeSolves
            .Include(s => s.PenaltyType)
            .Where(s => s.SessionId == sessionId)
            .OrderByDescending(s => s.SolvedAt)
            .Take(take)
            .ToListAsync();

    public async Task<IReadOnlyList<PracticeSolve>> GetAllSolvesAsync(Guid sessionId)
        => await _db.PracticeSolves
            .Include(s => s.PenaltyType)
            .Where(s => s.SessionId == sessionId)
            .OrderBy(s => s.SolvedAt)
            .ToListAsync();

    public async Task<PenaltyType?> GetPenaltyTypeByCodeAsync(string code)
        => await _db.PenaltyTypes
            .FirstOrDefaultAsync(p => p.Code == code);

    // ── History ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PracticeSession>> GetSessionsByUserAsync(
        Guid userId, Guid? puzzleTypeId, int skip, int take)
    {
        var query = _db.PracticeSessions
            .Include(s => s.PuzzleType)
            .Where(s => s.UserId == userId);

        if (puzzleTypeId.HasValue)
            query = query.Where(s => s.PuzzleTypeId == puzzleTypeId.Value);

        return await query
            .OrderByDescending(s => s.StartedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<int> CountSolvesAsync(Guid sessionId)
        => await _db.PracticeSolves.CountAsync(s => s.SessionId == sessionId);
}
