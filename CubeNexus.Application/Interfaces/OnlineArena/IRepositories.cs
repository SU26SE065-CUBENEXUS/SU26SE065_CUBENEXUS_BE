using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.OnlineArena;

public interface IOnlineProfileRepository
{
    Task<OnlineProfile?> GetProfileAsync(Guid userId, Guid puzzleTypeId);
    Task<List<OnlineProfile>> GetUserProfilesAsync(Guid userId);
    Task<List<OnlineProfile>> GetLeaderboardAsync(Guid puzzleTypeId, int top = 100);
    Task AddAsync(OnlineProfile profile);
    void Update(OnlineProfile profile);
}

public interface IMatchmakingQueueRepository
{
    Task<MatchmakingQueue?> GetQueuedQueueAsync(Guid userId, Guid puzzleTypeId);
    Task<MatchmakingQueue?> GetLatestNonCancelledQueueAsync(Guid userId, Guid puzzleTypeId);
    Task<MatchmakingQueue?> FindMatchForUpdateAsync(Guid puzzleTypeId, Guid currentUserId, int currentElo, int eloRange);
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

public interface IFraudReportRepository
{
    Task<FraudReport?> GetByIdAsync(Guid id);
    Task<List<FraudReport>> GetPendingReportsAsync();
    Task<List<FraudReport>> GetByMatchAsync(Guid matchId);
    Task AddAsync(FraudReport report);
    void Update(FraudReport report);
}
