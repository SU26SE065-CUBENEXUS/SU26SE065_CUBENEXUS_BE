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
    // ── Repositories cho Puzzle & Practice ────────────────────
    IPuzzleTypeRepository PuzzleTypes { get; }
    IPracticeRepository   Practice    { get; }

    // ── Repositories cho Elo Arena ─────────────────────────────
    IOnlineProfileRepository OnlineProfiles { get; }
    IEloSeedingRepository    EloSeeding     { get; }
    IEloConfigRepository     EloConfigs     { get; }

    // ── Generic Repositories cho các entity còn lại ────────────
    IRepository<EloHistory>      EloHistories      { get; }
    IRepository<OnlineMatch>     OnlineMatches     { get; }
    IRepository<EloSeedThreshold> EloSeedThresholds { get; }

    // ── Persist ────────────────────────────────────────────────
    /// <summary>
    /// Lưu tất cả thay đổi trong một transaction.
    /// Gọi 1 lần duy nhất sau khi hoàn thành toàn bộ logic.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
