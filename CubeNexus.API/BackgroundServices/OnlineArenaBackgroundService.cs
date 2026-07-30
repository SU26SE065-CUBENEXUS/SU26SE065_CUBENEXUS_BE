using CubeNexus.Application.UseCases.OnlineArena;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CubeNexus.API.BackgroundServices;

/// <summary>
/// BackgroundService chạy mỗi 120 giây để tự động chuyển phase theo deadline.
/// Tất cả transitions đều idempotent — chạy nhiều lần không gây tác dụng phụ.
/// 
/// Phase transitions handled (deadline-based only — auto-ready is event-driven in use cases):
///   ROOM_SETUP / WEBRTC_CONNECTING / MOBILE_TIMER_PAIRING / SCRAMBLE_CHECKING
///     → setupDeadlineAt hết → CANCELLED (setup timeout, per-player cooldown)
///   COUNTDOWN
///     → countdownEndsAt hết → check BothRecordingStarted:
///        → both recording → INSPECTION
///        → missing recording → CANCELLED (failed-player attributed cooldown)
///   INSPECTION
///     → inspectionDeadlineAt hết → SOLVING
///   SOLVING
///     → solveDeadlineAt hết → auto-DNF pending players
///   FINISH_CHECKING / PENDING_EVIDENCE
///     → deadline hết → reconcile or NEEDS_REVIEW
///   COMPLETED / CANCELLED / NEEDS_REVIEW / DRAW
///     → skip (terminal)
/// NOTE: BothChecklistPassed is NOT polled here — auto-ready triggers in MarkCamera/WebRtc/Timer/Scramble use cases. </summary>
public class OnlineArenaBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OnlineArenaBackgroundService> _logger;
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(15);

    public OnlineArenaBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<OnlineArenaBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OnlineArenaBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunReconcileLoopAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnlineArenaBackgroundService reconcile loop.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("OnlineArenaBackgroundService stopped.");
    }

    private async Task RunReconcileLoopAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var matchRepo = scope.ServiceProvider.GetRequiredService<IOnlineMatchRepository>();
        var confirmationRepo = scope.ServiceProvider.GetRequiredService<IOnlineMatchConfirmationRepository>();
        var queueRepo = scope.ServiceProvider.GetRequiredService<IMatchmakingQueueRepository>();
        var profileRepo = scope.ServiceProvider.GetRequiredService<IOnlineProfileRepository>();
        var notifier = scope.ServiceProvider.GetRequiredService<IOnlineArenaRealtimeNotifier>();
        var uow = scope.ServiceProvider.GetRequiredService<CubeNexus.Application.Interfaces.IUnitOfWork>();

        var timeoutUseCase = new ApplyConfirmationTimeoutUseCase(
            confirmationRepo, queueRepo, profileRepo, notifier, uow);

        try
        {
            // 1. Handle expired match confirmations first
            await ProcessExpiredConfirmationsAsync(confirmationRepo, timeoutUseCase, ct);

            // 2. Handle match lifecycle (setup timeout, solve timeout, etc.)
            await ProcessMatchLifecycleAsync(scope, matchRepo, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in matchmaking reconcile loop");
        }
    }

    private static async Task ProcessExpiredConfirmationsAsync(
        IOnlineMatchConfirmationRepository confirmationRepo,
        ApplyConfirmationTimeoutUseCase timeoutUseCase,
        CancellationToken ct)
    {
        var expired = await confirmationRepo.GetExpiredPendingConfirmationsAsync(DateTime.UtcNow);
        foreach (var confirmation in expired)
        {
            await timeoutUseCase.ExecuteAsync(confirmation, ct);
        }
    }

    private async Task ProcessMatchLifecycleAsync(IServiceScope scope, IOnlineMatchRepository matchRepo, CancellationToken ct)
    {
        var setupTimeoutUseCase = scope.ServiceProvider.GetRequiredService<ApplySetupTimeoutUseCase>();
        var solveTimeoutUseCase = scope.ServiceProvider.GetRequiredService<ApplySolveTimeoutUseCase>();
        var transitionToSolvingUseCase = scope.ServiceProvider.GetRequiredService<TransitionToSolvingUseCase>();
        var startMatchUseCase = scope.ServiceProvider.GetRequiredService<StartOnlineMatchUseCase>();
        var reconcileUseCase = scope.ServiceProvider.GetRequiredService<ReconcileOnlineMatchStatusUseCase>();

        var activeMatches = await matchRepo.GetActiveMatchesForReconcileAsync(ct);
        var now = DateTime.UtcNow;

        foreach (var match in activeMatches)
        {
            try
            {
                // Skip terminal
                if (match.StatusCode is nameof(OnlineMatchStatus.COMPLETED)
                    or nameof(OnlineMatchStatus.CANCELLED)
                    or nameof(OnlineMatchStatus.DRAW))
                    continue;

                switch (match.Phase)
                {
                    // ===== SETUP PHASE =====
                    case "ROOM_SETUP":
                    case "WEBRTC_CONNECTING":
                    case "MOBILE_TIMER_PAIRING":
                    case "SCRAMBLE_CHECKING":
                    {
                        // Check setup deadline timeout — only deadline-based cancel here
                        // Auto-ready (checklist completion) is event-driven in use cases, NOT polled here
                        if (match.SetupDeadlineAt.HasValue && now >= match.SetupDeadlineAt.Value)
                        {
                            _logger.LogInformation("Match {MatchId} setup timeout. Cancelling.", match.Id);
                            await setupTimeoutUseCase.ExecuteAsync(match, ct);
                        }
                        break;
                    }

                    // ===== COUNTDOWN PHASE =====
                    case "COUNTDOWN":
                    {
                        if (match.CountdownEndsAt.HasValue && now >= match.CountdownEndsAt.Value)
                        {
                            _logger.LogInformation("Match {MatchId} countdown ended → checking recording status.", match.Id);
                            await startMatchUseCase.TransitionToInspectionAsync(match);
                        }
                        break;
                    }

                    // ===== INSPECTION PHASE =====
                    case "INSPECTION":
                    {
                        if (match.InspectionDeadlineAt.HasValue && now >= match.InspectionDeadlineAt.Value)
                        {
                            _logger.LogInformation("Match {MatchId} inspection ended → SOLVING.", match.Id);
                            await transitionToSolvingUseCase.ExecuteAsync(match, ct);
                        }
                        break;
                    }

                    // ===== SOLVING PHASE =====
                    case "SOLVING":
                    {
                        if (match.SolveDeadlineAt.HasValue && now >= match.SolveDeadlineAt.Value)
                        {
                            _logger.LogInformation("Match {MatchId} solve timeout. Applying DNF.", match.Id);
                            await solveTimeoutUseCase.ExecuteAsync(match, ct);
                        }
                        break;
                    }

                    // ===== FINISH_CHECKING PHASE =====
                    case "FINISH_CHECKING":
                    {
                        if (match.FinishCheckDeadlineAt.HasValue && now >= match.FinishCheckDeadlineAt.Value)
                        {
                            _logger.LogInformation("Match {MatchId} finish check deadline → reconcile.", match.Id);
                            // Admin userId placeholder — reconcile is internal
                            try { await reconcileUseCase.ExecuteAsync(Guid.Empty, match.Id, true); }
                            catch (Exception ex) { _logger.LogWarning(ex, "Reconcile failed for match {MatchId}.", match.Id); }
                        }
                        break;
                    }

                    // ===== PENDING_EVIDENCE PHASE =====
                    case "PENDING_EVIDENCE":
                    {
                        if (match.VideoEvidenceUploadDeadlineAt.HasValue
                            && now >= match.VideoEvidenceUploadDeadlineAt.Value)
                        {
                            _logger.LogInformation("Match {MatchId} video evidence deadline → reconcile.", match.Id);
                            try { await reconcileUseCase.ExecuteAsync(Guid.Empty, match.Id, true); }
                            catch (Exception ex) { _logger.LogWarning(ex, "Reconcile failed for match {MatchId}.", match.Id); }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing match {MatchId} in background reconcile.", match.Id);
            }
        }
    }
}
