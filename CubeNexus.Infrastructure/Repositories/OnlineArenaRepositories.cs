using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

public class MatchmakingQueueRepository : IMatchmakingQueueRepository
{
    private readonly ApplicationDbContext _context;

    public MatchmakingQueueRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<MatchmakingQueue?> GetQueuedQueueAsync(Guid userId, Guid puzzleTypeId)
        => _context.Set<MatchmakingQueue>()
            .FirstOrDefaultAsync(queue =>
                queue.UserId == userId
                && queue.PuzzleTypeId == puzzleTypeId
                && queue.StatusCode == "QUEUED");

    public Task<MatchmakingQueue?> GetConfirmingQueueAsync(Guid userId, Guid puzzleTypeId)
        => _context.Set<MatchmakingQueue>()
            .FirstOrDefaultAsync(queue =>
                queue.UserId == userId
                && queue.PuzzleTypeId == puzzleTypeId
                && queue.StatusCode == "CONFIRMING");

    public Task<MatchmakingQueue?> GetLatestNonCancelledQueueAsync(Guid userId, Guid puzzleTypeId)
        => _context.Set<MatchmakingQueue>()
            .Where(queue =>
                queue.UserId == userId
                && queue.PuzzleTypeId == puzzleTypeId
                && queue.StatusCode != "CANCELLED")
            .OrderByDescending(queue => queue.QueuedAt)
            .FirstOrDefaultAsync();

    public async Task<MatchmakingQueue?> FindMatchForUpdateAsync(Guid puzzleTypeId, Guid currentUserId, int currentElo, int eloRange)
    {
        // ORDER BY queued_at ASC, id ASC ensures deterministic pick order when timestamps are equal
        // (e.g. 4 users join simultaneously — id is the tie-breaker)
        // FOR UPDATE SKIP LOCKED prevents two concurrent transactions from grabbing the same row
        const string sql = """
            SELECT * FROM matchmaking_queue
            WHERE status_code = 'QUEUED'
              AND puzzle_type_id = {0}
              AND user_id <> {1}
            ORDER BY queued_at ASC, id ASC
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """;

        var queue = await _context.Set<MatchmakingQueue>()
            .FromSqlRaw(sql, puzzleTypeId, currentUserId)
            .Include(item => item.OnlineProfile)
                .ThenInclude(op => op.User)
            .FirstOrDefaultAsync();

        if (queue != null && Math.Abs(queue.OnlineProfile.EloStandard - currentElo) <= eloRange)
            return queue;

        return null;
    }

    /// <summary>
    /// Re-checks inside an open transaction whether the user already has an active
    /// (QUEUED or CONFIRMING) queue entry. Used to guard against race conditions
    /// where two concurrent FindMatch requests for the same user both pass the
    /// pre-transaction check but only one should succeed.
    /// </summary>
    public Task<MatchmakingQueue?> GetActiveQueueInsideTransactionAsync(Guid userId, Guid puzzleTypeId)
        => _context.Set<MatchmakingQueue>()
            .FirstOrDefaultAsync(q =>
                q.UserId == userId
                && q.PuzzleTypeId == puzzleTypeId
                && (q.StatusCode == "QUEUED" || q.StatusCode == "CONFIRMING"));

    public Task AddAsync(MatchmakingQueue queue)
        => _context.Set<MatchmakingQueue>().AddAsync(queue).AsTask();

    public void Update(MatchmakingQueue queue)
        => _context.Set<MatchmakingQueue>().Update(queue);
}

public class OnlineMatchRepository : IOnlineMatchRepository
{
    private static readonly string[] ActiveStatuses = ["CREATED", "READY", "ONGOING", "PENDING_EVIDENCE", "NEEDS_REVIEW"];
    private static readonly string[] ReconcilePhases =
    [
        "ROOM_SETUP",
        "WEBRTC_CONNECTING",
        "MOBILE_TIMER_PAIRING",
        "SCRAMBLE_CHECKING",
        "COUNTDOWN",
        "INSPECTION",
        "SOLVING",
        "FINISH_CHECKING",
        "PENDING_EVIDENCE"
    ];

    private readonly ApplicationDbContext _context;

    public OnlineMatchRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<OnlineMatch?> GetByIdAsync(Guid id)
        => _context.Set<OnlineMatch>().FirstOrDefaultAsync(match => match.Id == id);

    public Task<OnlineMatch?> GetByIdWithPlayersAsync(Guid id)
        => _context.Set<OnlineMatch>()
            .Include(match => match.Player1)
            .Include(match => match.Player2)
            .FirstOrDefaultAsync(match => match.Id == id);

    public Task<OnlineMatch?> GetByRoomTokenAsync(string roomToken)
        => _context.Set<OnlineMatch>().FirstOrDefaultAsync(match => match.RoomToken == roomToken);

    public Task<OnlineMatch?> GetByQrSessionCodeAsync(string qrSessionCode)
        => _context.Set<OnlineMatch>().FirstOrDefaultAsync(match => match.QrSessionCode == qrSessionCode);

    public Task<OnlineMatch?> GetLatestActiveMatchAsync(Guid userId, Guid puzzleTypeId)
        => _context.Set<OnlineMatch>()
            .Where(match =>
                match.PuzzleTypeId == puzzleTypeId
                && (match.Player1Id == userId || match.Player2Id == userId)
                && ActiveStatuses.Contains(match.StatusCode))
            .OrderByDescending(match => match.CreatedAt)
            .FirstOrDefaultAsync();

    public Task<OnlineMatch?> GetLatestMatchAsync(Guid userId, Guid puzzleTypeId)
        => _context.Set<OnlineMatch>()
            .Where(match =>
                match.PuzzleTypeId == puzzleTypeId
                && (match.Player1Id == userId || match.Player2Id == userId))
            .OrderByDescending(match => match.CreatedAt)
            .FirstOrDefaultAsync();

    public Task<bool> HasActiveMatchAsync(Guid userId, Guid puzzleTypeId)
        => _context.Set<OnlineMatch>().AnyAsync(match =>
            match.PuzzleTypeId == puzzleTypeId
            && (match.Player1Id == userId || match.Player2Id == userId)
            && ActiveStatuses.Contains(match.StatusCode));

    public Task<List<OnlineMatch>> GetActiveMatchesForReconcileAsync(CancellationToken ct = default)
        => _context.Set<OnlineMatch>()
            // Only load phases handled by OnlineArenaBackgroundService.
            // NEEDS_REVIEW remains an active business status for user flows,
            // but the reconciler intentionally has no work to perform there.
            .Where(m => ActiveStatuses.Contains(m.StatusCode)
                && ReconcilePhases.Contains(m.Phase))
            .ToListAsync(ct);

    public async Task<(List<OnlineMatch> Items, int TotalCount)> GetUserMatchHistoryAsync(Guid userId, Guid? puzzleTypeId, int page, int pageSize)
    {
        var query = _context.Set<OnlineMatch>()
            .AsNoTracking()
            .Include(m => m.PuzzleType)
            .Include(m => m.Player1)
            .Include(m => m.Player2)
            .Include(m => m.VideoEvidences)
            .Where(m => m.Player1Id == userId || m.Player2Id == userId);

        if (puzzleTypeId.HasValue && puzzleTypeId.Value != Guid.Empty)
        {
            query = query.Where(m => m.PuzzleTypeId == puzzleTypeId.Value);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<bool> MarkRecordingStartedAsync(Guid matchId, Guid playerId, DateTime recordingStartedAt)
    {
        var query = _context.Set<OnlineMatch>().Where(match => match.Id == matchId);
        var participant = await query.Select(match => new
        {
            IsPlayer1 = match.Player1Id == playerId,
            IsPlayer2 = match.Player2Id == playerId
        }).FirstOrDefaultAsync();

        if (participant == null)
            return false;

        var affected = participant.IsPlayer1
            ? await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(match => match.Player1RecordingStarted, true)
                .SetProperty(match => match.Player1RecordingStartedAt, recordingStartedAt))
            : participant.IsPlayer2
                ? await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(match => match.Player2RecordingStarted, true)
                .SetProperty(match => match.Player2RecordingStartedAt, recordingStartedAt))
                : 0;

        return affected == 1;
    }

    public Task AddAsync(OnlineMatch match)
        => _context.Set<OnlineMatch>().AddAsync(match).AsTask();

    public void Update(OnlineMatch match)
        => _context.Set<OnlineMatch>().Update(match);
}

public class MobileTimerSessionRepository : IMobileTimerSessionRepository
{
    private readonly ApplicationDbContext _context;

    public MobileTimerSessionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<MobileTimerSession?> GetSessionAsync(Guid matchId, Guid userId)
        => _context.Set<MobileTimerSession>()
            .FirstOrDefaultAsync(session => session.MatchId == matchId && session.UserId == userId);

    public Task AddAsync(MobileTimerSession session)
        => _context.Set<MobileTimerSession>().AddAsync(session).AsTask();

    public void Update(MobileTimerSession session)
        => _context.Set<MobileTimerSession>().Update(session);
}

public class OnlineMatchAiCheckRepository : IOnlineMatchAiCheckRepository
{
    private readonly ApplicationDbContext _context;

    public OnlineMatchAiCheckRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task AddAsync(OnlineMatchAiCheck check)
        => _context.Set<OnlineMatchAiCheck>().AddAsync(check).AsTask();

    public Task<List<OnlineMatchAiCheck>> GetByMatchAsync(Guid matchId)
        => _context.Set<OnlineMatchAiCheck>()
            .Where(item => item.MatchId == matchId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

    public Task<OnlineMatchAiCheck?> GetLatestAsync(Guid matchId, Guid playerId, string checkType)
        => _context.Set<OnlineMatchAiCheck>()
            .Where(item => item.MatchId == matchId && item.PlayerId == playerId && item.CheckType == checkType)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync();
}

public class OnlineMatchVideoEvidenceRepository : IOnlineMatchVideoEvidenceRepository
{
    private readonly ApplicationDbContext _context;

    public OnlineMatchVideoEvidenceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task AddAsync(OnlineMatchVideoEvidence evidence)
        => _context.Set<OnlineMatchVideoEvidence>().AddAsync(evidence).AsTask();

    public Task<OnlineMatchVideoEvidence?> GetLatestAsync(Guid matchId, Guid playerId)
        => _context.Set<OnlineMatchVideoEvidence>()
            .Where(item => item.MatchId == matchId && item.PlayerId == playerId)
            .OrderByDescending(item => item.UploadedAt)
            .FirstOrDefaultAsync();

    public Task<List<OnlineMatchVideoEvidence>> GetByMatchAsync(Guid matchId)
        => _context.Set<OnlineMatchVideoEvidence>()
            .Where(item => item.MatchId == matchId)
            .OrderBy(item => item.UploadedAt)
            .ToListAsync();

    public void Update(OnlineMatchVideoEvidence evidence)
        => _context.Set<OnlineMatchVideoEvidence>().Update(evidence);
}

public class OnlineMatchAuditLogRepository : IOnlineMatchAuditLogRepository
{
    private readonly ApplicationDbContext _context;

    public OnlineMatchAuditLogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task AddAsync(OnlineMatchAuditLog log)
        => _context.Set<OnlineMatchAuditLog>().AddAsync(log).AsTask();

    public Task<List<OnlineMatchAuditLog>> GetByMatchAsync(Guid matchId)
        => _context.Set<OnlineMatchAuditLog>()
            .Where(item => item.MatchId == matchId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
}

public class EloHistoryRepository : IEloHistoryRepository
{
    private readonly ApplicationDbContext _context;

    public EloHistoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task AddAsync(EloHistory history)
        => _context.Set<EloHistory>().AddAsync(history).AsTask();
}

public class OnlineMatchConfirmationRepository : IOnlineMatchConfirmationRepository
{
    private readonly ApplicationDbContext _context;

    public OnlineMatchConfirmationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<OnlineMatchConfirmation?> GetByIdAsync(Guid id)
        => _context.Set<OnlineMatchConfirmation>()
            .Include(c => c.Player1)
            .Include(c => c.Player2)
            .FirstOrDefaultAsync(c => c.Id == id);

    public async Task<OnlineMatchConfirmation?> GetByIdForUpdateAsync(Guid id)
    {
        // Raw SQL with FOR UPDATE to acquire a row-level lock inside a transaction.
        // Prevents two concurrent Confirm calls from both creating an OnlineMatch.
        const string sql = """
            SELECT * FROM online_match_confirmations
            WHERE id = {0}
            FOR UPDATE
            """;

        var confirmation = await _context.Set<OnlineMatchConfirmation>()
            .FromSqlRaw(sql, id)
            .FirstOrDefaultAsync();

        if (confirmation == null) return null;

        // Load navigation properties separately (EF doesn't support Include on FromSqlRaw with row-locks)
        await _context.Entry(confirmation).Reference(c => c.Player1).LoadAsync();
        await _context.Entry(confirmation).Reference(c => c.Player2).LoadAsync();
        return confirmation;
    }

    public Task<OnlineMatchConfirmation?> GetPendingConfirmationAsync(Guid userId, Guid puzzleTypeId)
        => _context.Set<OnlineMatchConfirmation>()
            .Include(c => c.Player1)
            .Include(c => c.Player2)
            .FirstOrDefaultAsync(c =>
                c.PuzzleTypeId == puzzleTypeId
                && (c.Player1UserId == userId || c.Player2UserId == userId)
                && c.Status == "PENDING");

    public Task<List<OnlineMatchConfirmation>> GetExpiredPendingConfirmationsAsync(DateTime now)
        => _context.Set<OnlineMatchConfirmation>()
            .Include(c => c.Player1)
            .Include(c => c.Player2)
            .Where(c => c.Status == "PENDING" && c.ConfirmDeadlineAt <= now)
            .ToListAsync();

    public Task AddAsync(OnlineMatchConfirmation confirmation)
        => _context.Set<OnlineMatchConfirmation>().AddAsync(confirmation).AsTask();

    public void Update(OnlineMatchConfirmation confirmation)
        => _context.Set<OnlineMatchConfirmation>().Update(confirmation);
}

public class FraudReportRepository : IFraudReportRepository
{
    private readonly ApplicationDbContext _context;

    public FraudReportRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<FraudReport?> GetByIdAsync(Guid id)
        => _context.Set<FraudReport>().FirstOrDefaultAsync(report => report.Id == id);

    public Task<List<FraudReport>> GetPendingReportsAsync()
        => _context.Set<FraudReport>()
            .Where(report => report.StatusCode == "OPEN" || report.StatusCode == "REVIEWING" || report.StatusCode == "PENDING")
            .OrderBy(report => report.CreatedAt)
            .ToListAsync();

    public Task<List<FraudReport>> GetByMatchAsync(Guid matchId)
        => _context.Set<FraudReport>()
            .Where(report => report.MatchId == matchId)
            .OrderBy(report => report.CreatedAt)
            .ToListAsync();

    public Task AddAsync(FraudReport report)
        => _context.Set<FraudReport>().AddAsync(report).AsTask();

    public void Update(FraudReport report)
        => _context.Set<FraudReport>().Update(report);
}
