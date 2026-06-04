using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Repositories;
using CubeNexus.Infrastructure.Persistence;

namespace CubeNexus.Infrastructure;

/// <summary>
/// Unit of Work: gom tất cả repositories, chia sẻ chung 1 DbContext instance.
/// Mọi thay đổi từ các repository đều được commit cùng lúc qua SaveChangesAsync().
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _db;

    // Lazy initialization – chỉ tạo repository khi cần
    private IPuzzleTypeRepository?          _puzzleTypes;
    private IPracticeRepository?            _practice;
    private IOnlineProfileRepository?       _onlineProfiles;
    private IEloSeedingRepository?          _eloSeeding;
    private IEloConfigRepository?           _eloConfigs;
    private IRepository<EloHistory>?        _eloHistories;
    private IRepository<OnlineMatch>?       _onlineMatches;
    private IRepository<EloSeedThreshold>?  _eloSeedThresholds;

    public UnitOfWork(ApplicationDbContext db)
    {
        _db = db;
    }

    // ── Repositories ──────────────────────────────────────────

    public IPuzzleTypeRepository PuzzleTypes
        => _puzzleTypes ??= new PuzzleTypeRepository(_db);

    public IPracticeRepository Practice
        => _practice ??= new PracticeRepository(_db);

    public IOnlineProfileRepository OnlineProfiles
        => _onlineProfiles ??= new OnlineProfileRepository(_db);

    public IEloSeedingRepository EloSeeding
        => _eloSeeding ??= new EloSeedingRepository(_db);

    public IEloConfigRepository EloConfigs
        => _eloConfigs ??= new EloConfigRepository(_db);

    public IRepository<EloHistory> EloHistories
        => _eloHistories ??= new Repository<EloHistory>(_db);

    public IRepository<OnlineMatch> OnlineMatches
        => _onlineMatches ??= new Repository<OnlineMatch>(_db);

    public IRepository<EloSeedThreshold> EloSeedThresholds
        => _eloSeedThresholds ??= new Repository<EloSeedThreshold>(_db);

    // ── Persist ───────────────────────────────────────────────

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);

    // ── Dispose ───────────────────────────────────────────────

    public void Dispose() => _db.Dispose();
}
