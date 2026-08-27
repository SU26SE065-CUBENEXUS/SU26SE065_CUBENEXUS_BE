using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.OnlineArena;

public interface IOnlineProfileRepository
{
    Task<OnlineProfile?> GetProfileAsync(Guid userId, Guid puzzleTypeId);
    Task<OnlineProfile?> GetByUserIdAsync(Guid userId);
    Task<List<OnlineProfile>> GetUserProfilesAsync(Guid userId);
    Task<List<OnlineProfile>> GetLeaderboardAsync(Guid puzzleTypeId, int top = 100);
    Task AddAsync(OnlineProfile profile);
    void Update(OnlineProfile profile);
}

public interface IMatchmakingQueueRepository
{
    Task<MatchmakingQueue?> GetQueuedQueueAsync(Guid userId, Guid puzzleTypeId);
    Task<MatchmakingQueue?> GetConfirmingQueueAsync(Guid userId, Guid puzzleTypeId);
    Task<MatchmakingQueue?> GetLatestNonCancelledQueueAsync(Guid userId, Guid puzzleTypeId);
    Task<MatchmakingQueue?> FindMatchForUpdateAsync(Guid puzzleTypeId, Guid currentUserId, int currentElo, int eloRange);
    /// <summary>
    /// Re-checks inside an open transaction whether the user already has an active
    /// (QUEUED or CONFIRMING) queue entry. Guards against concurrent FindMatch requests
    /// for the same user that both pass the pre-transaction check.
    /// </summary>
    Task<MatchmakingQueue?> GetActiveQueueInsideTransactionAsync(Guid userId, Guid puzzleTypeId);
    Task AddAsync(MatchmakingQueue queue);
    void Update(MatchmakingQueue queue);
}

public interface IOnlineMatchRepository
{
    Task<OnlineMatch?> GetByIdAsync(Guid id);
    Task<OnlineMatch?> GetByIdWithPlayersAsync(Guid id);
    Task<OnlineMatch?> GetByRoomTokenAsync(string roomToken);
    Task<OnlineMatch?> GetByQrSessionCodeAsync(string qrSessionCode);
    Task<OnlineMatch?> GetLatestActiveMatchAsync(Guid userId, Guid puzzleTypeId);
    Task<OnlineMatch?> GetLatestMatchAsync(Guid userId, Guid puzzleTypeId);
    Task<bool> HasActiveMatchAsync(Guid userId, Guid puzzleTypeId);
    /// <summary>Fetch all non-terminal matches for BackgroundService reconciliation.</summary>
    Task<List<OnlineMatch>> GetActiveMatchesForReconcileAsync(CancellationToken ct = default);
    Task<(List<OnlineMatch> Items, int TotalCount)> GetUserMatchHistoryAsync(Guid userId, Guid? puzzleTypeId, int page, int pageSize);
    Task<bool> MarkRecordingStartedAsync(Guid matchId, Guid playerId, DateTime recordingStartedAt);
    Task AddAsync(OnlineMatch match);
    void Update(OnlineMatch match);
}

public interface IOnlineMatchAiCheckRepository
{
    Task AddAsync(OnlineMatchAiCheck check);
    Task<List<OnlineMatchAiCheck>> GetByMatchAsync(Guid matchId);
    Task<OnlineMatchAiCheck?> GetLatestAsync(Guid matchId, Guid playerId, string checkType);
}

public interface IOnlineMatchVideoEvidenceRepository
{
    Task AddAsync(OnlineMatchVideoEvidence evidence);
    Task<OnlineMatchVideoEvidence?> GetLatestAsync(Guid matchId, Guid playerId);
    Task<List<OnlineMatchVideoEvidence>> GetByMatchAsync(Guid matchId);
    void Update(OnlineMatchVideoEvidence evidence);
}

public interface IOnlineMatchAuditLogRepository
{
    Task AddAsync(OnlineMatchAuditLog log);
    Task<List<OnlineMatchAuditLog>> GetByMatchAsync(Guid matchId);
}

public interface IMobileTimerSessionRepository
{
    Task<MobileTimerSession?> GetSessionAsync(Guid matchId, Guid userId);
    Task AddAsync(MobileTimerSession session);
    void Update(MobileTimerSession session);
}

public interface IEloHistoryRepository
{
    Task AddAsync(EloHistory history);
}

public interface IOnlineMatchConfirmationRepository
{
    /// <summary>Returns the confirmation with Player1 and Player2 navigation loaded.</summary>
    Task<OnlineMatchConfirmation?> GetByIdAsync(Guid id);
    /// <summary>
    /// Returns the confirmation row with a FOR UPDATE lock — must be called inside an open transaction.
    /// Used by Confirm API to prevent duplicate OnlineMatch creation under concurrent requests.
    /// </summary>
    Task<OnlineMatchConfirmation?> GetByIdForUpdateAsync(Guid id);
    /// <summary>Returns any PENDING confirmation that the given player is part of.</summary>
    Task<OnlineMatchConfirmation?> GetPendingConfirmationAsync(Guid userId, Guid puzzleTypeId);
    /// <summary>Returns all PENDING confirmations whose deadline has passed.</summary>
    Task<List<OnlineMatchConfirmation>> GetExpiredPendingConfirmationsAsync(DateTime now);
    Task AddAsync(OnlineMatchConfirmation confirmation);
    void Update(OnlineMatchConfirmation confirmation);
}

public interface IFraudReportRepository
{
    Task<FraudReport?> GetByIdAsync(Guid id);
    Task<List<FraudReport>> GetPendingReportsAsync();
    Task<List<FraudReport>> GetAllReportsAsync(string? status = null);
    Task<List<FraudReport>> GetByMatchAsync(Guid matchId);
    Task AddAsync(FraudReport report);
    void Update(FraudReport report);
}
