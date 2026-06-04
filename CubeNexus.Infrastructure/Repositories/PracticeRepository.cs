using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

public class PracticeRepository : IPracticeRepository
{
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

    // ── Solve ─────────────────────────────────────────────────────────────────

    public async Task AddSolveAsync(PracticeSolve solve)
        => await _db.PracticeSolves.AddAsync(solve);

    public async Task<IReadOnlyList<PracticeSolve>> GetLatestSolvesAsync(Guid sessionId, int take)
        => await _db.PracticeSolves
            .Where(s => s.SessionId == sessionId)
            .OrderByDescending(s => s.SolvedAt)
            .Take(take)
            .ToListAsync();

    public async Task<IReadOnlyList<PracticeSolve>> GetAllSolvesAsync(Guid sessionId)
        => await _db.PracticeSolves
            .Where(s => s.SessionId == sessionId)
            .OrderBy(s => s.SolvedAt)
            .ToListAsync();

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
