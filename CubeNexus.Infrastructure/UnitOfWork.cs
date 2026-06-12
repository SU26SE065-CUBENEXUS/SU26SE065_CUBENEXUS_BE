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
    private ITournamentRepository?          _tournaments;
    private IRegistrationRepository?        _registrations;
    private IRepository<OfflineRegistrationEvent>? _offlineRegistrationEvents;
    private IPuzzleTypeRepository?          _puzzleTypes;
    private IPracticeRepository?            _practice;
    private IRepository<PenaltyType>?       _penaltyTypes;
    private IOnlineProfileRepository?       _onlineProfiles;
    private IEloSeedingRepository?          _eloSeeding;
    private IEloConfigRepository?           _eloConfigs;
    private IRepository<EloHistory>?        _eloHistories;
    private IRepository<OnlineMatch>?       _onlineMatches;
    private IRepository<EloSeedThreshold>?  _eloSeedThresholds;
    private IRepository<Result>?            _results;
    private IRepository<GroupCompetitor>?   _groupCompetitors;
    private IRepository<Group>?             _groups;
    private IRepository<Event>?             _events;
    private IRepository<PracticeAo5Snapshot>? _practiceAo5Snapshots;
    private IRepository<User>?              _users;
    private IRepository<ScrambleSet>?       _scrambleSets;
    private IRepository<Scramble>?          _scrambles;
    private IRepository<MedleyResultDetail>? _medleyResultDetails;
    private IRepository<MedleyEventPuzzle>? _medleyEventPuzzles;

    public UnitOfWork(ApplicationDbContext db)
    {
        _db = db;
    }

    // ── Repositories ──────────────────────────────────────────

    public ITournamentRepository Tournaments
        => _tournaments ??= new TournamentRepository(_db);

    public IRegistrationRepository Registrations
        => _registrations ??= new RegistrationRepository(_db);

    public IRepository<OfflineRegistrationEvent> OfflineRegistrationEvents
        => _offlineRegistrationEvents ??= new Repository<OfflineRegistrationEvent>(_db);

    public IPuzzleTypeRepository PuzzleTypes
        => _puzzleTypes ??= new PuzzleTypeRepository(_db);

    public IPracticeRepository Practice
        => _practice ??= new PracticeRepository(_db);

    public IRepository<PenaltyType> PenaltyTypes
        => _penaltyTypes ??= new Repository<PenaltyType>(_db);

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

    public IRepository<Result> Results
        => _results ??= new Repository<Result>(_db);

    public IRepository<GroupCompetitor> GroupCompetitors
        => _groupCompetitors ??= new Repository<GroupCompetitor>(_db);

    public IRepository<Group> Groups
        => _groups ??= new Repository<Group>(_db);

    public IRepository<Event> Events
        => _events ??= new Repository<Event>(_db);

    public IRepository<PracticeAo5Snapshot> PracticeAo5Snapshots
        => _practiceAo5Snapshots ??= new Repository<PracticeAo5Snapshot>(_db);

    public IRepository<User> Users
        => _users ??= new Repository<User>(_db);

    public IRepository<ScrambleSet> ScrambleSets
        => _scrambleSets ??= new Repository<ScrambleSet>(_db);

    public IRepository<Scramble> Scrambles
        => _scrambles ??= new Repository<Scramble>(_db);

    public IRepository<MedleyResultDetail> MedleyResultDetails
        => _medleyResultDetails ??= new Repository<MedleyResultDetail>(_db);

    public IRepository<MedleyEventPuzzle> MedleyEventPuzzles
        => _medleyEventPuzzles ??= new Repository<MedleyEventPuzzle>(_db);

    private IRepository<Dispute>? _disputes;

    public IRepository<Dispute> Disputes
        => _disputes ??= new Repository<Dispute>(_db);

    private IRepository<ResultAuditLog>? _resultAuditLogs;

    public IRepository<ResultAuditLog> ResultAuditLogs
        => _resultAuditLogs ??= new Repository<ResultAuditLog>(_db);

    // ── Persist ───────────────────────────────────────────────

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _db.SaveChangesAsync(ct);

    // ── Dispose ───────────────────────────────────────────────

    public void Dispose() => _db.Dispose();
}
