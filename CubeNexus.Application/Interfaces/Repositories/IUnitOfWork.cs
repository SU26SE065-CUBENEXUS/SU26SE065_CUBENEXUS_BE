using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

public interface IUnitOfWork : IDisposable
{
    ITournamentRepository Tournaments { get; }
    IRegistrationRepository Registrations { get; }
    IRepository<OfflineRegistrationEvent> OfflineRegistrationEvents { get; }

    IPuzzleTypeRepository PuzzleTypes { get; }
    IPracticeRepository Practice { get; }
    IRepository<PenaltyType> PenaltyTypes { get; }

    IOnlineProfileRepository OnlineProfiles { get; }
    IEloConfigRepository EloConfigs { get; }

    IRepository<EloHistory> EloHistories { get; }
    IRepository<OnlineMatch> OnlineMatches { get; }
    IRepository<Result> Results { get; }
    IRepository<GroupCompetitor> GroupCompetitors { get; }
    IRepository<Group> Groups { get; }
    IRepository<Event> Events { get; }
    IRepository<User> Users { get; }
    IRepository<ScrambleSet> ScrambleSets { get; }
    IRepository<Scramble> Scrambles { get; }
    IRepository<MedleyResultDetail> MedleyResultDetails { get; }
    IRepository<MedleyEventPuzzle> MedleyEventPuzzles { get; }
    IRepository<Dispute> Disputes { get; }
    IRepository<ResultAuditLog> ResultAuditLogs { get; }
    IRepository<TournamentJudge> TournamentJudges { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
