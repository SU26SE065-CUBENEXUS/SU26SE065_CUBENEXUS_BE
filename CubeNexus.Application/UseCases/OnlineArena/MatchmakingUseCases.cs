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

    public FindOnlineMatchUseCase(
        IMatchmakingQueueRepository queueRepo,
        IOnlineMatchConfirmationRepository confirmationRepo,
        IOnlineProfileRepository profileRepo,
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        CubeNexus.Application.Interfaces.Repositories.IPuzzleTypeRepository puzzleTypeRepo)
    {
        _queueRepo = queueRepo;
        _confirmationRepo = confirmationRepo;
        _profileRepo = profileRepo;
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
        _puzzleTypeRepo = puzzleTypeRepo;
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
                MeUserId = userId,
                ServerNow = now
            };
        }

        // 4. Already queued?
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

        // 5. Try to match — inside a serializable transaction with row-lock
        await _uow.BeginTransactionAsync();
        try
        {
            var opponentQueue = await _queueRepo.FindMatchForUpdateAsync(puzzleTypeId, userId, profile.EloStandard, 200);
            if (opponentQueue == null)
            {
                // No opponent yet — join the queue
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

            // Opponent found — move BOTH queue entries to CONFIRMING (not MATCHED yet)
            opponentQueue.StatusCode = MatchmakingQueueStatus.CONFIRMING.ToString();
            _queueRepo.Update(opponentQueue);

            // Current player also needs a queue entry marked CONFIRMING
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

            var basePayload = new
            {
                confirmationId = confirmation.Id,
                confirmDeadlineAt = deadline,
                remainingSeconds,
                player1Confirmed = false,
                player2Confirmed = false,
                serverNow = DateTime.UtcNow
            };

            var p1Payload = new
            {
                confirmationId = confirmation.Id,
                opponent = new
                {
                    userId = userId,  // player2 = current user = opponent for p1
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
                    userId = opponentQueue.UserId,  // player1 = opponent for p2
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

            // Load opponent profile for the return value
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

        // Notify both players — requeueAvailable so frontend can show "Search again" button
        await _notifier.NotifyMatchConfirmationExpiredAsync(
            confirmation.Player1UserId,
            confirmation.Player2UserId,
            new
            {
                confirmationId = confirmation.Id,
                reason,
                requeueAvailable = true,  // backend does NOT auto-requeue
                player1Confirmed = p1DidConfirm,
                player2Confirmed = p2DidConfirm,
                serverNow = DateTime.UtcNow
            });

        // Apply cooldown notification to non-confirming players
        if (!p1DidConfirm)
        {
            var p1Profile = await _profileRepo.GetProfileAsync(confirmation.Player1UserId, confirmation.PuzzleTypeId);
            if (p1Profile != null)
                await _notifier.NotifyMatchmakingCooldownAppliedAsync(confirmation.Player1UserId, new
                {
                    reason = "FAILED_TO_CONFIRM",
                    cooldownUntil = p1Profile.MatchmakingCooldownUntil,
                    serverNow = DateTime.UtcNow
                });
        }

        if (!p2DidConfirm)
        {
            var p2Profile = await _profileRepo.GetProfileAsync(confirmation.Player2UserId, confirmation.PuzzleTypeId);
            if (p2Profile != null)
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
