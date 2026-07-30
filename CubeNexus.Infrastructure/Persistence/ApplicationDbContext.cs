using CubeNexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // 1. Master Data & Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<PuzzleType> PuzzleTypes => Set<PuzzleType>();
    public DbSet<PenaltyType> PenaltyTypes => Set<PenaltyType>();
    public DbSet<EloConfig> EloConfigs => Set<EloConfig>();

    // 2. Offline Tournament
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentManager> TournamentManagers => Set<TournamentManager>();
    public DbSet<TournamentJudge> TournamentJudges => Set<TournamentJudge>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<MedleyEventPuzzle> MedleyEventPuzzles => Set<MedleyEventPuzzle>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<OfflineRegistrationEvent> OfflineRegistrationEvents => Set<OfflineRegistrationEvent>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupCompetitor> GroupCompetitors => Set<GroupCompetitor>();
    public DbSet<ScrambleSet> ScrambleSets => Set<ScrambleSet>();
    public DbSet<Scramble> Scrambles => Set<Scramble>();
    public DbSet<Result> Results => Set<Result>();
    public DbSet<MedleyResultDetail> MedleyResultDetails => Set<MedleyResultDetail>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<ResultAuditLog> ResultAuditLogs => Set<ResultAuditLog>();

    // 3. Online Arena
    public DbSet<OnlineProfile> OnlineProfiles => Set<OnlineProfile>();
    public DbSet<MatchmakingQueue> MatchmakingQueues => Set<MatchmakingQueue>();
    public DbSet<OnlineMatchConfirmation> OnlineMatchConfirmations => Set<OnlineMatchConfirmation>();
    public DbSet<OnlineMatch> OnlineMatches => Set<OnlineMatch>();
    public DbSet<OnlineMatchAiCheck> OnlineMatchAiChecks => Set<OnlineMatchAiCheck>();
    public DbSet<OnlineMatchVideoEvidence> OnlineMatchVideoEvidences => Set<OnlineMatchVideoEvidence>();
    public DbSet<OnlineMatchAuditLog> OnlineMatchAuditLogs => Set<OnlineMatchAuditLog>();
    public DbSet<MobileTimerSession> MobileTimerSessions => Set<MobileTimerSession>();
    public DbSet<EloHistory> EloHistories => Set<EloHistory>();
    public DbSet<FraudReport> FraudReports => Set<FraudReport>();


    // 5. Practice
    public DbSet<PracticeSession> PracticeSessions => Set<PracticeSession>();
    public DbSet<PracticeAttempt> PracticeAttempts => Set<PracticeAttempt>();
    public DbSet<PracticeSolve> PracticeSolves => Set<PracticeSolve>();

    // 6. Notifications
    public DbSet<Notification> Notifications => Set<Notification>();

    // 7. Refresh Tokens
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // 8. User Tokens (email confirmation, password reset)
    public DbSet<UserToken> UserTokens => Set<UserToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map entity to table names
        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<PuzzleType>().ToTable("puzzle_types");
        modelBuilder.Entity<PenaltyType>().ToTable("penalty_types");
        modelBuilder.Entity<EloConfig>().ToTable("elo_config");
        modelBuilder.Entity<Tournament>().ToTable("tournaments");
        modelBuilder.Entity<TournamentManager>().ToTable("tournament_managers");
        modelBuilder.Entity<TournamentJudge>().ToTable("tournament_judges");
        modelBuilder.Entity<Event>().ToTable("events");
        modelBuilder.Entity<MedleyEventPuzzle>().ToTable("medley_event_puzzles");
        modelBuilder.Entity<Registration>().ToTable("registrations");
        modelBuilder.Entity<OfflineRegistrationEvent>().ToTable("offline_registration_events");
        modelBuilder.Entity<Group>().ToTable("groups");
        modelBuilder.Entity<GroupCompetitor>().ToTable("group_competitors");
        modelBuilder.Entity<ScrambleSet>().ToTable("scramble_sets");
        modelBuilder.Entity<Scramble>().ToTable("scrambles");
        modelBuilder.Entity<Result>().ToTable("results");
        modelBuilder.Entity<MedleyResultDetail>().ToTable("medley_result_details");
        modelBuilder.Entity<Dispute>().ToTable("disputes");
        modelBuilder.Entity<ResultAuditLog>().ToTable("result_audit_logs");
        modelBuilder.Entity<OnlineProfile>().ToTable("online_profiles");
        modelBuilder.Entity<MatchmakingQueue>().ToTable("matchmaking_queue");
        modelBuilder.Entity<OnlineMatchConfirmation>().ToTable("online_match_confirmations");
        modelBuilder.Entity<OnlineMatch>().ToTable("online_matches");
        modelBuilder.Entity<OnlineMatchAiCheck>().ToTable("online_match_ai_checks");
        modelBuilder.Entity<OnlineMatchVideoEvidence>().ToTable("online_match_video_evidence");
        modelBuilder.Entity<OnlineMatchAuditLog>().ToTable("online_match_audit_logs");
        modelBuilder.Entity<MobileTimerSession>().ToTable("mobile_timer_sessions");
        modelBuilder.Entity<EloHistory>().ToTable("elo_history");
        modelBuilder.Entity<FraudReport>().ToTable("fraud_reports");

        modelBuilder.Entity<PracticeSession>().ToTable("practice_sessions");
        modelBuilder.Entity<PracticeAttempt>().ToTable("practice_attempts");
        modelBuilder.Entity<PracticeSolve>().ToTable("practice_solves");
        modelBuilder.Entity<Notification>().ToTable("notifications");
        modelBuilder.Entity<RefreshToken>().ToTable("refresh_tokens");
        modelBuilder.Entity<UserToken>().ToTable("user_tokens");

        // Configure relationships with multiple FK to User
        modelBuilder.Entity<Tournament>(entity =>
        {
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedBy);
        });

        modelBuilder.Entity<TournamentManager>(entity =>
        {
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.Tournament).WithMany().HasForeignKey(e => e.TournamentId);
        });

        modelBuilder.Entity<TournamentJudge>(entity =>
        {
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.Tournament).WithMany().HasForeignKey(e => e.TournamentId);
        });

        modelBuilder.Entity<Registration>(entity =>
        {
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.Tournament).WithMany(t => t.Registrations).HasForeignKey(e => e.TournamentId);
            // Index to speed up participant count queries per tournament
            entity.HasIndex(e => new { e.TournamentId, e.StatusCode }).HasDatabaseName("ix_registrations_tournament_status");
        });

        modelBuilder.Entity<OfflineRegistrationEvent>(entity =>
        {
            entity.HasOne(e => e.Registration).WithMany(r => r.OfflineRegistrationEvents).HasForeignKey(e => e.RegistrationId);
            entity.HasOne(e => e.Event).WithMany().HasForeignKey(e => e.EventId);
        });

        modelBuilder.Entity<GroupCompetitor>(entity =>
        {
            entity.HasOne(e => e.Group).WithMany().HasForeignKey(e => e.GroupId);
            entity.HasOne(e => e.OfflineRegistrationEvent).WithMany().HasForeignKey(e => e.RegistrationEventId);
            entity.Property(e => e.StatusCode).HasConversion<string>();
        });

        modelBuilder.Entity<ScrambleSet>(entity =>
        {
            entity.HasOne(e => e.GeneratedByUser).WithMany().HasForeignKey(e => e.GeneratedBy);
            entity.HasOne(e => e.Group).WithMany().HasForeignKey(e => e.GroupId);
        });

        modelBuilder.Entity<Result>(entity =>
        {
            entity.HasOne(e => e.JudgedByUser).WithMany().HasForeignKey(e => e.JudgedBy);
            entity.HasOne(e => e.GroupCompetitor).WithMany().HasForeignKey(e => e.GroupCompetitorId);
            entity.HasOne(e => e.Scramble).WithMany().HasForeignKey(e => e.ScrambleId);
            entity.HasOne(e => e.PenaltyType).WithMany().HasForeignKey(e => e.PenaltyTypeId);
        });

        modelBuilder.Entity<MedleyResultDetail>(entity =>
        {
            entity.HasOne(e => e.Result).WithMany().HasForeignKey(e => e.ResultId);
            entity.HasOne(e => e.MedleyPuzzle).WithMany().HasForeignKey(e => e.MedleyPuzzleId);
            entity.HasOne(e => e.Scramble).WithMany().HasForeignKey(e => e.ScrambleId);
            entity.HasOne(e => e.PenaltyType).WithMany().HasForeignKey(e => e.PenaltyTypeId);
        });

        modelBuilder.Entity<ResultAuditLog>(entity =>
        {
            entity.HasOne(e => e.Result).WithMany().HasForeignKey(e => e.ResultId);
            entity.HasOne(e => e.ChangedByUser).WithMany().HasForeignKey(e => e.ChangedBy);
            entity.HasOne(e => e.OldPenaltyType).WithMany().HasForeignKey(e => e.OldPenaltyTypeId);
            entity.HasOne(e => e.NewPenaltyType).WithMany().HasForeignKey(e => e.NewPenaltyTypeId);
        });

        modelBuilder.Entity<Dispute>(entity =>
        {
            entity.HasOne(e => e.ReportedByUser).WithMany().HasForeignKey(e => e.ReportedBy);
            entity.HasOne(e => e.ResolvedByUser).WithMany().HasForeignKey(e => e.ResolvedBy);
            entity.HasOne(e => e.Result).WithMany().HasForeignKey(e => e.ResultId);
        });

        modelBuilder.Entity<OnlineMatch>(entity =>
        {
            entity.HasOne(e => e.Player1).WithMany().HasForeignKey(e => e.Player1Id);
            entity.HasOne(e => e.Player2).WithMany().HasForeignKey(e => e.Player2Id);
            entity.HasOne(e => e.Winner).WithMany().HasForeignKey(e => e.WinnerId);
            entity.HasOne(e => e.TimeoutPlayer).WithMany().HasForeignKey(e => e.TimeoutPlayerId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.PuzzleType).WithMany().HasForeignKey(e => e.PuzzleTypeId);
            // Player profile FK — cần map rõ ràng vì EF không tự suy ra từ tên
            entity.HasOne(e => e.Player1Profile).WithMany().HasForeignKey(e => e.Player1ProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Player2Profile).WithMany().HasForeignKey(e => e.Player2ProfileId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OnlineMatchAiCheck>(entity =>
        {
            entity.HasOne(e => e.Match).WithMany(m => m.AiChecks).HasForeignKey(e => e.MatchId);
            entity.HasOne(e => e.Player).WithMany().HasForeignKey(e => e.PlayerId);
            entity.HasOne(e => e.VideoEvidence).WithMany().HasForeignKey(e => e.VideoEvidenceId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OnlineMatchVideoEvidence>(entity =>
        {
            entity.HasOne(e => e.Match).WithMany(m => m.VideoEvidences).HasForeignKey(e => e.MatchId);
            entity.HasOne(e => e.Player).WithMany().HasForeignKey(e => e.PlayerId);
        });

        modelBuilder.Entity<OnlineMatchAuditLog>(entity =>
        {
            entity.HasOne(e => e.Match).WithMany(m => m.AuditLogs).HasForeignKey(e => e.MatchId);
            entity.HasOne(e => e.Player).WithMany().HasForeignKey(e => e.PlayerId);
        });

        modelBuilder.Entity<MobileTimerSession>(entity =>
        {
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.Match).WithMany().HasForeignKey(e => e.MatchId);
        });

        modelBuilder.Entity<FraudReport>(entity =>
        {
            entity.HasOne(e => e.ReportedByUser).WithMany().HasForeignKey(e => e.ReporterUserId);
            entity.HasOne(e => e.AccusedUser).WithMany().HasForeignKey(e => e.ReportedUserId);
            entity.HasOne(e => e.ReviewedByUser).WithMany().HasForeignKey(e => e.ReviewedBy);
            entity.HasOne(e => e.ResolvedByAdmin).WithMany().HasForeignKey(e => e.ResolvedByAdminId);
            entity.HasOne(e => e.Match).WithMany().HasForeignKey(e => e.MatchId);
        });


        modelBuilder.Entity<OnlineProfile>(entity =>
        {
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<OnlineMatchConfirmation>(entity =>
        {
            entity.HasOne(e => e.Player1).WithMany().HasForeignKey(e => e.Player1UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Player2).WithMany().HasForeignKey(e => e.Player2UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.PuzzleType).WithMany().HasForeignKey(e => e.PuzzleTypeId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Match).WithMany().HasForeignKey(e => e.MatchId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MatchmakingQueue>(entity =>
        {
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.OnlineProfile).WithMany().HasForeignKey(e => e.OnlineProfileId);
            entity.HasOne(e => e.PuzzleType).WithMany().HasForeignKey(e => e.PuzzleTypeId);
        });

        modelBuilder.Entity<EloHistory>(entity =>
        {
            entity.HasOne(e => e.OnlineProfile).WithMany().HasForeignKey(e => e.OnlineProfileId);
            entity.HasOne(e => e.Match).WithMany().HasForeignKey(e => e.MatchId);
        });

        modelBuilder.Entity<EloConfig>(entity =>
        {
            entity.HasOne(e => e.UpdatedByUser).WithMany().HasForeignKey(e => e.UpdatedBy);
        });

        modelBuilder.Entity<PracticeSession>(entity =>
        {
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.PuzzleType).WithMany().HasForeignKey(e => e.PuzzleTypeId);
        });

        modelBuilder.Entity<PracticeAttempt>(entity =>
        {
            entity.Property(e => e.State).HasConversion<string>();
            entity.HasOne(e => e.Session).WithMany().HasForeignKey(e => e.SessionId);
            entity.HasOne(e => e.PenaltyType).WithMany().HasForeignKey(e => e.PenaltyTypeId);
            entity.HasOne(e => e.Solve)
                .WithOne(s => s.Attempt)
                .HasForeignKey<PracticeSolve>(s => s.AttemptId);
        });

        modelBuilder.Entity<PracticeSolve>(entity =>
        {
            entity.HasOne(e => e.PenaltyType).WithMany().HasForeignKey(e => e.PenaltyTypeId);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<UserToken>(entity =>
        {
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        // Configure column name mappings (snake_case)
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                // Convert PascalCase to snake_case
                var snakeCaseName = ConvertToSnakeCase(property.Name);
                property.SetColumnName(snakeCaseName);
            }
        }

        modelBuilder.Entity<RefreshToken>()
            .Property(r => r.Token)
            .HasColumnName("token_hash");

        modelBuilder.Entity<RefreshToken>()
            .Property(r => r.ReplacedBy)
            .HasColumnName("replaced_by_token_hash");
    }

    private static string ConvertToSnakeCase(string name)
    {
        return string.Concat(
            name.Select((c, i) =>
                i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])
                    ? "_" + c.ToString().ToLower()
                    : i > 0 && char.IsUpper(c) && char.IsUpper(name[i - 1]) && i + 1 < name.Length && !char.IsUpper(name[i + 1])
                        ? "_" + c.ToString().ToLower()
                        : c.ToString().ToLower()
            )
        );
    }
}
