using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

// =====================================================================
// FindOnlineMatchUseCase
// QUEUED -> MATCH_FOUND (with confirmation) -> (ConfirmOnlineMatchUseCase) -> MATCHED
// =====================================================================
public class FindOnlineMatchUseCase
{
    private readonly IMatchmakingQueueRepository _queueRepo;
    private readonly IOnlineMatchConfirmationRepository _confirmationRepo;
    private readonly IOnlineProfileRepository _profileRepo;
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly CubeNexus.Application.Interfaces.Repositories.IPuzzleTypeRepository _puzzleTypeRepo;
    private readonly CubeNexus.Application.Interfaces.Services.IScramblePoolService _scramblePool;

    public FindOnlineMatchUseCase(
        IMatchmakingQueueRepository queueRepo,
        IOnlineMatchConfirmationRepository confirmationRepo,
        IOnlineProfileRepository profileRepo,
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        CubeNexus.Application.Interfaces.Repositories.IPuzzleTypeRepository puzzleTypeRepo,
        CubeNexus.Application.Interfaces.Services.IScramblePoolService scramblePool)
    {
        _queueRepo = queueRepo;
        _confirmationRepo = confirmationRepo;
        _profileRepo = profileRepo;
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
        _puzzleTypeRepo = puzzleTypeRepo;
        _scramblePool = scramblePool;
    }

    public async Task<MatchmakingStatusDto> ExecuteAsync(Guid userId, Guid puzzleTypeId)
    {
        var now = DateTime.UtcNow;

        // 1. Check active match (already past confirmation — in actual match)
        var activeMatch = await _matchRepo.GetLatestActiveMatchAsync(userId, puzzleTypeId);
        if (activeMatch != null)
        {
            return new MatchmakingStatusDto
            {
                Status = activeMatch.StatusCode == OnlineMatchStatus.CREATED.ToString() ? "MATCHED" : "IN_ACTIVE_MATCH",
                MatchId = activeMatch.Id,
                MatchStatus = activeMatch.StatusCode,
                RoomToken = activeMatch.RoomToken,
                QrSessionCode = activeMatch.QrSessionCode,
                MeUserId = userId,
                OpponentUserId = activeMatch.Player1Id == userId ? activeMatch.Player2Id : activeMatch.Player1Id,
                SetupDeadlineAt = activeMatch.SetupDeadlineAt,
                ServerNow = now
            };
        }

        var availability = await _scramblePool.GetOnlineMatchAvailabilityAsync(puzzleTypeId);
        if (!availability.IsAvailable)
            throw new InvalidOperationException(availability.Message ?? "Online matches are temporarily unavailable.");

        // 2. Cooldown check
        var profile = await _profileRepo.GetProfileAsync(userId, puzzleTypeId);
        if (profile == null)
            throw new InvalidOperationException("Online profile not initialized for this puzzle type.");

        if (profile.MatchmakingCooldownUntil.HasValue && profile.MatchmakingCooldownUntil.Value > now)
        {
            var remaining = (int)(profile.MatchmakingCooldownUntil.Value - now).TotalSeconds;
            return new MatchmakingStatusDto
            {
                Status = "COOLDOWN",
                CooldownUntil = profile.MatchmakingCooldownUntil,
                RemainingSeconds = remaining,
                ServerNow = now,
                MeUserId = userId
            };
        }

        // 3. Already in a pending confirmation window?
        var existingConfirmation = await _confirmationRepo.GetPendingConfirmationAsync(userId, puzzleTypeId);
        if (existingConfirmation != null)
        {
            var isPlayer1 = existingConfirmation.Player1UserId == userId;
            var opponent = isPlayer1 ? existingConfirmation.Player2 : existingConfirmation.Player1;
            var opponentProfile = await _profileRepo.GetProfileAsync(opponent.Id, puzzleTypeId);
            var remaining = Math.Max(0, (int)(existingConfirmation.ConfirmDeadlineAt - now).TotalSeconds);

            return new MatchmakingStatusDto
            {
                Status = "MATCH_FOUND",
                ConfirmationId = existingConfirmation.Id,
                Opponent = new OpponentDto
                {
                    UserId = opponent.Id,
                    DisplayName = opponent.DisplayName,
                    Rating = opponentProfile?.EloStandard ?? 0
                },
                ConfirmDeadlineAt = existingConfirmation.ConfirmDeadlineAt,
                RemainingSeconds = remaining,
                Player1Confirmed = existingConfirmation.Player1Confirmed,
                Player2Confirmed = existingConfirmation.Player2Confirmed,
                IsPlayer1 = isPlayer1,
                MeUserId = userId,
                ServerNow = now
            };
        }

        // *** Step 4 (early-return for QUEUED) intentionally removed ***
        //
        // WHY: When multiple users join simultaneously (e.g. A, B, C, D), all 4 may find
        // an empty queue and all insert QUEUED entries in the same instant — leaving all 4
        // stuck QUEUED forever, because the old step-4 early-return prevented them from
        // ever re-running the matching transaction on subsequent polls.
        //
        // FIX: Always proceed to the transaction, even if the user is already QUEUED.
        // Inside the transaction we handle the already-QUEUED case idempotently:
        //   • No opponent found + already QUEUED → commit with no changes (stay QUEUED)
        //   • Opponent found + already QUEUED    → update existing entry to CONFIRMING

        // 5. Transaction: try to match or join queue (idempotent if already QUEUED)
        await _uow.BeginTransactionAsync();
        try
        {
            // Guard: if a CONFIRMING entry exists inside the transaction, another concurrent
            // request already matched this user — return early without double-processing.
            var confirmingEntry = await _queueRepo.GetConfirmingQueueAsync(userId, puzzleTypeId);
            if (confirmingEntry != null)
            {
                await _uow.RollbackTransactionAsync();
                // Re-read the confirmation that the other concurrent request created
                var latestConf = await _confirmationRepo.GetPendingConfirmationAsync(userId, puzzleTypeId);
                if (latestConf != null)
                {
                    var isP1 = latestConf.Player1UserId == userId;
                    var opp = isP1 ? latestConf.Player2 : latestConf.Player1;
                    var oppProf = await _profileRepo.GetProfileAsync(opp.Id, puzzleTypeId);
                    return new MatchmakingStatusDto
                    {
                        Status = "MATCH_FOUND",
                        ConfirmationId = latestConf.Id,
                        Opponent = new OpponentDto
                        {
                            UserId = opp.Id,
                            DisplayName = opp.DisplayName,
                            Rating = oppProf?.EloStandard ?? 0
                        },
                        ConfirmDeadlineAt = latestConf.ConfirmDeadlineAt,
                        RemainingSeconds = Math.Max(0, (int)(latestConf.ConfirmDeadlineAt - DateTime.UtcNow).TotalSeconds),
                        Player1Confirmed = latestConf.Player1Confirmed,
                        Player2Confirmed = latestConf.Player2Confirmed,
                        IsPlayer1 = isP1,
                        MeUserId = userId,
                        ServerNow = DateTime.UtcNow
                    };
                }
                return new MatchmakingStatusDto { Status = "QUEUED", ServerNow = DateTime.UtcNow, MeUserId = userId };
            }

            // Check for user's own existing QUEUED entry (may have been inserted by a previous call)
            var myExistingQueue = await _queueRepo.GetQueuedQueueAsync(userId, puzzleTypeId);

            // Try to find an opponent (ORDER BY queued_at ASC, id ASC; FOR UPDATE SKIP LOCKED)
            var opponentQueue = await _queueRepo.FindMatchForUpdateAsync(puzzleTypeId, userId, profile.EloStandard, 200);

            if (opponentQueue == null)
            {
                // No opponent available right now.
                if (myExistingQueue == null)
                {
                    // Not yet queued — insert a new QUEUED entry.
                    var newQueue = new MatchmakingQueue
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        OnlineProfileId = profile.Id,
                        PuzzleTypeId = puzzleTypeId,
                        QueuedAt = DateTime.UtcNow,
                        StatusCode = MatchmakingQueueStatus.QUEUED.ToString()
                    };
                    await _queueRepo.AddAsync(newQueue);
                    await _uow.CommitTransactionAsync();

                    await _notifier.NotifyMatchmakingQueuedAsync(userId, new
                    {
                        status = "QUEUED",
                        queueId = newQueue.Id,
                        puzzleTypeId,
                        userId,
                        serverNow = DateTime.UtcNow
                    });

                    return new MatchmakingStatusDto
                    {
                        Status = "QUEUED",
                        QueueId = newQueue.Id,
                        ServerNow = DateTime.UtcNow
                    };
                }
                else
                {
                    // Already QUEUED — commit with no changes (idempotent keep-alive).
                    await _uow.CommitTransactionAsync();
                    return new MatchmakingStatusDto
                    {
                        Status = "QUEUED",
                        QueueId = myExistingQueue.Id,
                        ServerNow = DateTime.UtcNow
                    };
                }
            }

            // ── Opponent found ─────────────────────────────────────────────────────────
            // Mark opponent's QUEUED entry as CONFIRMING.
            opponentQueue.StatusCode = MatchmakingQueueStatus.CONFIRMING.ToString();
            _queueRepo.Update(opponentQueue);

            // Handle current user's queue entry:
            //   • If already QUEUED (concurrent insert from earlier) → update to CONFIRMING.
            //   • If not yet in queue (found opponent on first call) → insert CONFIRMING.
            if (myExistingQueue != null)
            {
                myExistingQueue.StatusCode = MatchmakingQueueStatus.CONFIRMING.ToString();
                _queueRepo.Update(myExistingQueue);
            }
            else
            {
                var myQueue = new MatchmakingQueue
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    OnlineProfileId = profile.Id,
                    PuzzleTypeId = puzzleTypeId,
                    QueuedAt = DateTime.UtcNow,
                    StatusCode = MatchmakingQueueStatus.CONFIRMING.ToString()
                };
                await _queueRepo.AddAsync(myQueue);
            }

            // Create the confirmation record
            var deadline = DateTime.UtcNow.Add(MatchmakingCooldownPolicy.ConfirmationWindow);
            var confirmation = new OnlineMatchConfirmation
            {
                Id = Guid.NewGuid(),
                PuzzleTypeId = puzzleTypeId,
                Player1UserId = opponentQueue.UserId,
                Player2UserId = userId,
                Player1Confirmed = false,
                Player2Confirmed = false,
                ConfirmDeadlineAt = deadline,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };
            await _confirmationRepo.AddAsync(confirmation);
            await _uow.CommitTransactionAsync();

            // Build per-player payloads (each player sees the other as "opponent")
            var opponentProfile = opponentQueue.OnlineProfile;
            var remainingSeconds = (int)(deadline - DateTime.UtcNow).TotalSeconds;

            var p1Payload = new
            {
                confirmationId = confirmation.Id,
                opponent = new
                {
                    userId = userId,
                    displayName = profile.User?.DisplayName ?? string.Empty,
                    rating = profile.EloStandard
                },
                confirmDeadlineAt = deadline,
                remainingSeconds,
                player1Confirmed = false,
                player2Confirmed = false,
                meUserId = opponentQueue.UserId,
                serverNow = DateTime.UtcNow
            };

            var p2Payload = new
            {
                confirmationId = confirmation.Id,
                opponent = new
                {
                    userId = opponentQueue.UserId,
                    displayName = opponentProfile?.User?.DisplayName ?? string.Empty,
                    rating = opponentProfile?.EloStandard ?? 0
                },
                confirmDeadlineAt = deadline,
                remainingSeconds,
                player1Confirmed = false,
                player2Confirmed = false,
                meUserId = userId,
                serverNow = DateTime.UtcNow
            };

            await _notifier.NotifyMatchFoundAsync(
                opponentQueue.UserId, userId,
                p1Payload, p2Payload);

            var opponentUser = opponentQueue.User;
            return new MatchmakingStatusDto
            {
                Status = "MATCH_FOUND",
                ConfirmationId = confirmation.Id,
                Opponent = new OpponentDto
                {
                    UserId = opponentQueue.UserId,
                    DisplayName = opponentUser?.DisplayName ?? string.Empty,
                    Rating = opponentProfile?.EloStandard ?? 0
                },
                ConfirmDeadlineAt = deadline,
                RemainingSeconds = remainingSeconds,
                Player1Confirmed = false,
                Player2Confirmed = false,
                IsPlayer1 = false,
                MeUserId = userId,
                ServerNow = DateTime.UtcNow
            };
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }
}

// =====================================================================
// CancelMatchmakingUseCase
// - If in PENDING confirmation: cancel confirmation, penalize canceller
// - If in QUEUED: cancel queue entry
// =====================================================================
public class CancelMatchmakingUseCase
{
    private readonly IMatchmakingQueueRepository _queueRepo;
    private readonly IOnlineMatchConfirmationRepository _confirmationRepo;
    private readonly IOnlineProfileRepository _profileRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public CancelMatchmakingUseCase(
        IMatchmakingQueueRepository queueRepo,
        IOnlineMatchConfirmationRepository confirmationRepo,
        IOnlineProfileRepository profileRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _queueRepo = queueRepo;
        _confirmationRepo = confirmationRepo;
        _profileRepo = profileRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task ExecuteAsync(Guid userId, Guid puzzleTypeId)
    {
        var now = DateTime.UtcNow;

        // 1. Cancel active confirmation if present
        var confirmation = await _confirmationRepo.GetPendingConfirmationAsync(userId, puzzleTypeId);
        if (confirmation != null)
        {
            confirmation.Status = "CANCELLED";
            _confirmationRepo.Update(confirmation);

            // Cancel both CONFIRMING queue entries
            var myQueue = await _queueRepo.GetConfirmingQueueAsync(userId, puzzleTypeId);
            if (myQueue != null)
            {
                myQueue.StatusCode = MatchmakingQueueStatus.CANCELLED.ToString();
                _queueRepo.Update(myQueue);
            }

            var opponentId = confirmation.Player1UserId == userId
                ? confirmation.Player2UserId
                : confirmation.Player1UserId;

            var opponentQueue = await _queueRepo.GetConfirmingQueueAsync(opponentId, puzzleTypeId);
            if (opponentQueue != null)
            {
                opponentQueue.StatusCode = MatchmakingQueueStatus.CANCELLED.ToString();
                _queueRepo.Update(opponentQueue);
            }

            // Apply light cooldown to the cancelling player only (not opponent)
            var cancellerProfile = await _profileRepo.GetProfileAsync(userId, puzzleTypeId);
            if (cancellerProfile != null)
            {
                cancellerProfile.MatchmakingCooldownUntil = now.Add(MatchmakingCooldownPolicy.CancelAfterMatchFound);
                _profileRepo.Update(cancellerProfile);
            }

            await _uow.SaveChangesAsync();

            // Notify both players
            await _notifier.NotifyMatchConfirmationCancelledAsync(
                confirmation.Player1UserId,
                confirmation.Player2UserId,
                new
                {
                    confirmationId = confirmation.Id,
                    cancelledByUserId = userId,
                    reason = "PLAYER_CANCELLED",
                    requeueAvailable = true,
                    serverNow = DateTime.UtcNow
                });

            // Notify the canceller they have a cooldown
            if (cancellerProfile?.MatchmakingCooldownUntil.HasValue == true)
            {
                await _notifier.NotifyMatchmakingCooldownAppliedAsync(userId, new
                {
                    reason = "CANCELLED_AFTER_MATCH_FOUND",
                    cooldownUntil = cancellerProfile.MatchmakingCooldownUntil,
                    serverNow = DateTime.UtcNow
                });
            }

            return;
        }

        // 2. Cancel QUEUED entry
        var queue = await _queueRepo.GetQueuedQueueAsync(userId, puzzleTypeId);
        if (queue == null)
            return;

        queue.StatusCode = MatchmakingQueueStatus.CANCELLED.ToString();
        _queueRepo.Update(queue);
        await _uow.SaveChangesAsync();

        await _notifier.NotifyMatchmakingCancelledAsync(userId, new
        {
            status = "CANCELLED",
            queueId = queue.Id,
            puzzleTypeId,
            serverNow = DateTime.UtcNow
        });
    }
}

// =====================================================================
// GetMatchmakingStatusUseCase
// Returns current matchmaking state for polling / reconnect
// =====================================================================
public class GetMatchmakingStatusUseCase
{
    private readonly IMatchmakingQueueRepository _queueRepo;
    private readonly IOnlineMatchConfirmationRepository _confirmationRepo;
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineProfileRepository _profileRepo;

    public GetMatchmakingStatusUseCase(
        IMatchmakingQueueRepository queueRepo,
        IOnlineMatchConfirmationRepository confirmationRepo,
        IOnlineMatchRepository matchRepo,
        IOnlineProfileRepository profileRepo)
    {
        _queueRepo = queueRepo;
        _confirmationRepo = confirmationRepo;
        _matchRepo = matchRepo;
        _profileRepo = profileRepo;
    }

    public async Task<MatchmakingStatusDto> ExecuteAsync(Guid userId, Guid puzzleTypeId)
    {
        var now = DateTime.UtcNow;

        // 1. Active match?
        var activeMatch = await _matchRepo.GetLatestActiveMatchAsync(userId, puzzleTypeId);
        if (activeMatch != null)
        {
            var status = activeMatch.StatusCode == OnlineMatchStatus.CREATED.ToString()
                ? "MATCHED"
                : "IN_ACTIVE_MATCH";

            return new MatchmakingStatusDto
            {
                Status = status,
                MatchId = activeMatch.Id,
                MatchStatus = activeMatch.StatusCode,
                RoomToken = activeMatch.RoomToken,
                QrSessionCode = status == "MATCHED" ? activeMatch.QrSessionCode : null,
                MeUserId = userId,
                OpponentUserId = activeMatch.Player1Id == userId ? activeMatch.Player2Id : activeMatch.Player1Id,
                SetupDeadlineAt = activeMatch.SetupDeadlineAt,
                ServerNow = now
            };
        }

        // 2. Cooldown?
        var profile = await _profileRepo.GetProfileAsync(userId, puzzleTypeId);
        if (profile?.MatchmakingCooldownUntil.HasValue == true && profile.MatchmakingCooldownUntil.Value > now)
        {
            var remaining = (int)(profile.MatchmakingCooldownUntil.Value - now).TotalSeconds;
            return new MatchmakingStatusDto
            {
                Status = "COOLDOWN",
                CooldownUntil = profile.MatchmakingCooldownUntil,
                RemainingSeconds = remaining,
                ServerNow = now,
                MeUserId = userId
            };
        }

        // 3. Active confirmation?
        var confirmation = await _confirmationRepo.GetPendingConfirmationAsync(userId, puzzleTypeId);
        if (confirmation != null)
        {
            var isPlayer1 = confirmation.Player1UserId == userId;
            var opponent = isPlayer1 ? confirmation.Player2 : confirmation.Player1;
            var opponentProfile = await _profileRepo.GetProfileAsync(opponent.Id, puzzleTypeId);
            var remaining = Math.Max(0, (int)(confirmation.ConfirmDeadlineAt - now).TotalSeconds);

            return new MatchmakingStatusDto
            {
                Status = "MATCH_FOUND",
                ConfirmationId = confirmation.Id,
                Opponent = new OpponentDto
                {
                    UserId = opponent.Id,
                    DisplayName = opponent.DisplayName,
                    Rating = opponentProfile?.EloStandard ?? 0
                },
                ConfirmDeadlineAt = confirmation.ConfirmDeadlineAt,
                RemainingSeconds = remaining,
                Player1Confirmed = confirmation.Player1Confirmed,
                Player2Confirmed = confirmation.Player2Confirmed,
                IsPlayer1 = isPlayer1,
                MeUserId = userId,
                ServerNow = now
            };
        }

        // 4. Queued?
        var queued = await _queueRepo.GetQueuedQueueAsync(userId, puzzleTypeId);
        if (queued != null)
        {
            return new MatchmakingStatusDto
            {
                Status = "QUEUED",
                QueueId = queued.Id,
                ServerNow = now
            };
        }

        return new MatchmakingStatusDto
        {
            Status = "IDLE",
            ServerNow = now
        };
    }
}

// =====================================================================
// ApplyConfirmationTimeoutUseCase
// Called by BackgroundService for each expired PENDING confirmation.
// DOES NOT create a match. Only penalizes non-confirming players.
// =====================================================================
public class ApplyConfirmationTimeoutUseCase
{
    private readonly IOnlineMatchConfirmationRepository _confirmationRepo;
    private readonly IMatchmakingQueueRepository _queueRepo;
    private readonly IOnlineProfileRepository _profileRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public ApplyConfirmationTimeoutUseCase(
        IOnlineMatchConfirmationRepository confirmationRepo,
        IMatchmakingQueueRepository queueRepo,
        IOnlineProfileRepository profileRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _confirmationRepo = confirmationRepo;
        _queueRepo = queueRepo;
        _profileRepo = profileRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task ExecuteAsync(OnlineMatchConfirmation confirmation, CancellationToken ct = default)
    {
        // IDEMPOTENCY: already handled
        if (confirmation.Status != "PENDING")
            return;

        var now = DateTime.UtcNow;
        confirmation.Status = "EXPIRED";
        _confirmationRepo.Update(confirmation);

        var p1DidConfirm = confirmation.Player1Confirmed;
        var p2DidConfirm = confirmation.Player2Confirmed;

        // Determine reason for frontend display
        var reason = (!p1DidConfirm && !p2DidConfirm)
            ? "BOTH_NOT_CONFIRMED"
            : "OPPONENT_NOT_CONFIRMED";

        // Player 1 handling
        await HandlePlayerOnExpiry(
            userId: confirmation.Player1UserId,
            puzzleTypeId: confirmation.PuzzleTypeId,
            didConfirm: p1DidConfirm,
            now: now,
            ct: ct);

        // Player 2 handling
        await HandlePlayerOnExpiry(
            userId: confirmation.Player2UserId,
            puzzleTypeId: confirmation.PuzzleTypeId,
            didConfirm: p2DidConfirm,
            now: now,
            ct: ct);

        await _uow.SaveChangesAsync(ct);

        var p1Profile = await _profileRepo.GetProfileAsync(confirmation.Player1UserId, confirmation.PuzzleTypeId);
        var p2Profile = await _profileRepo.GetProfileAsync(confirmation.Player2UserId, confirmation.PuzzleTypeId);

        // Notify both players — requeueAvailable so frontend can show "Search again" button
        await _notifier.NotifyMatchConfirmationExpiredAsync(
            confirmation.Player1UserId,
            confirmation.Player2UserId,
            new
            {
                confirmationId = confirmation.Id,
                reason,
                requeueAvailable = true,  // backend does NOT auto-requeue
                player1UserId = confirmation.Player1UserId,
                player2UserId = confirmation.Player2UserId,
                player1Confirmed = p1DidConfirm,
                player2Confirmed = p2DidConfirm,
                player1CooldownUntil = p1Profile?.MatchmakingCooldownUntil,
                player2CooldownUntil = p2Profile?.MatchmakingCooldownUntil,
                serverNow = DateTime.UtcNow
            });

        // Apply cooldown notification to non-confirming players
        if (!p1DidConfirm && p1Profile != null)
        {
            await _notifier.NotifyMatchmakingCooldownAppliedAsync(confirmation.Player1UserId, new
            {
                reason = "FAILED_TO_CONFIRM",
                cooldownUntil = p1Profile.MatchmakingCooldownUntil,
                serverNow = DateTime.UtcNow
            });
        }

        if (!p2DidConfirm && p2Profile != null)
        {
            await _notifier.NotifyMatchmakingCooldownAppliedAsync(confirmation.Player2UserId, new
            {
                reason = "FAILED_TO_CONFIRM",
                cooldownUntil = p2Profile.MatchmakingCooldownUntil,
                serverNow = DateTime.UtcNow
            });
        }
    }

    private async Task HandlePlayerOnExpiry(Guid userId, Guid puzzleTypeId, bool didConfirm, DateTime now, CancellationToken ct)
    {
        // Cancel the CONFIRMING queue entry regardless
        var queue = await _queueRepo.GetConfirmingQueueAsync(userId, puzzleTypeId);
        if (queue != null)
        {
            queue.StatusCode = MatchmakingQueueStatus.CANCELLED.ToString();
            _queueRepo.Update(queue);
        }

        if (!didConfirm)
        {
            // Apply cooldown to players who failed to confirm
            var profile = await _profileRepo.GetProfileAsync(userId, puzzleTypeId);
            if (profile == null) return;

            profile.MatchmakingCooldownUntil = now.Add(MatchmakingCooldownPolicy.FailedToConfirm);
            _profileRepo.Update(profile);
        }
        // Players who DID confirm: no cooldown, queue entry cancelled — they are free to re-queue
    }
}

// Extension helper for anonymous type merging (used in payload building)
internal static class AnonymousObjectExtensions
{
    public static object Merge(this object obj1, object obj2)
    {
        var dict = new System.Collections.Generic.Dictionary<string, object?>();
        foreach (var prop in obj1.GetType().GetProperties())
            dict[prop.Name] = prop.GetValue(obj1);
        foreach (var prop in obj2.GetType().GetProperties())
            dict[prop.Name] = prop.GetValue(obj2);
        return dict;
    }
}
