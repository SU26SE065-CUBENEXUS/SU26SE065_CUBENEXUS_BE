using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

/// <summary>
/// Unit of Work: điều phối tất cả repositories và quản lý transaction.
///
/// Lợi ích:
/// - Service chỉ inject 1 interface (IUnitOfWork) thay vì nhiều repo.
/// - SaveChangesAsync() gom tất cả thay đổi trong 1 transaction.
/// - Test: mock IUnitOfWork để isolate business logic hoàn toàn.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    // ── Repositories cho Offline Tournament ───────────────────
    ITournamentRepository Tournaments { get; }
    IRegistrationRepository Registrations { get; }
    IRepository<OfflineRegistrationEvent> OfflineRegistrationEvents { get; }

    // ── Repositories cho Puzzle & Practice ────────────────────
    IPuzzleTypeRepository PuzzleTypes { get; }
    IPracticeRepository   Practice    { get; }
    IRepository<PenaltyType> PenaltyTypes { get; }

    // ── Repositories cho Elo Arena ─────────────────────────────
    IOnlineProfileRepository OnlineProfiles { get; }
    IEloSeedingRepository    EloSeeding     { get; }
    IEloConfigRepository     EloConfigs     { get; }

    // ── Generic Repositories cho các entity còn lại ────────────
    IRepository<EloHistory>      EloHistories      { get; }
    IRepository<OnlineMatch>     OnlineMatches     { get; }
    IRepository<EloSeedThreshold> EloSeedThresholds { get; }
    IRepository<Result>          Results           { get; }
    IRepository<GroupCompetitor> GroupCompetitors  { get; }
    IRepository<Group>           Groups            { get; }
    IRepository<Event>           Events            { get; }
    IRepository<PracticeAo5Snapshot> PracticeAo5Snapshots { get; }
    IRepository<User>            Users             { get; }
    IRepository<ScrambleSet>     ScrambleSets      { get; }
    IRepository<Scramble>        Scrambles         { get; }
    IRepository<MedleyResultDetail> MedleyResultDetails { get; }
    IRepository<MedleyEventPuzzle> MedleyEventPuzzles { get; }
    IRepository<Dispute>         Disputes          { get; }
    IRepository<ResultAuditLog>  ResultAuditLogs   { get; }

    // ── Persist ────────────────────────────────────────────────
    /// <summary>
    /// Lưu tất cả thay đổi trong một transaction.
    /// Gọi 1 lần duy nhất sau khi hoàn thành toàn bộ logic.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
