using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

// =====================================================================
// ConfirmOnlineMatchUseCase
//
// Handles POST /online-arena/matchmaking/confirm/{confirmationId}
//
// Concurrency strategy:
//   - The confirmation row is loaded with FOR UPDATE (row-level lock)
//     inside an explicit transaction, so only one concurrent request
//     can proceed to create an OnlineMatch.
//   - If MatchId is already populated (set by first concurrent request),
//     the second request returns the existing match (idempotent).
// =====================================================================
public class ConfirmOnlineMatchUseCase
{
    private readonly IOnlineMatchConfirmationRepository _confirmationRepo;
    private readonly IMatchmakingQueueRepository _queueRepo;
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineProfileRepository _profileRepo;
    private readonly CubeNexus.Application.Interfaces.Repositories.IPuzzleTypeRepository _puzzleTypeRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly IScramblePoolService _scramblePool;

    public ConfirmOnlineMatchUseCase(
        IOnlineMatchConfirmationRepository confirmationRepo,
        IMatchmakingQueueRepository queueRepo,
        IOnlineMatchRepository matchRepo,
        IOnlineProfileRepository profileRepo,
        CubeNexus.Application.Interfaces.Repositories.IPuzzleTypeRepository puzzleTypeRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        IScramblePoolService scramblePool)
    {
        _confirmationRepo = confirmationRepo;
        _queueRepo = queueRepo;
        _matchRepo = matchRepo;
        _profileRepo = profileRepo;
        _puzzleTypeRepo = puzzleTypeRepo;
        _notifier = notifier;
        _uow = uow;
        _scramblePool = scramblePool;
    }

    public async Task<MatchmakingStatusDto> ExecuteAsync(Guid userId, Guid confirmationId)
    {
        var now = DateTime.UtcNow;

        // === Begin transaction + acquire row lock ===
        await _uow.BeginTransactionAsync();
        try
        {
            var confirmation = await _confirmationRepo.GetByIdForUpdateAsync(confirmationId);

            if (confirmation == null)
                throw new KeyNotFoundException($"Confirmation {confirmationId} not found.");

            // Validate the requesting user belongs to this confirmation
            if (confirmation.Player1UserId != userId && confirmation.Player2UserId != userId)
                throw new UnauthorizedAccessException("You are not a participant of this confirmation.");

            // === Idempotency: already terminal ===
            if (confirmation.Status == "CANCELLED")
                return BuildTerminalStatus("CANCELLED", confirmation);

            if (confirmation.Status == "EXPIRED")
                return BuildTerminalStatus("EXPIRED", confirmation);

            if (confirmation.Status == "CONFIRMED")
            {
                // Match already created — return it (idempotent)
                return await BuildMatchedStatus(userId, confirmation);
            }

            // === Deadline check ===
            if (now > confirmation.ConfirmDeadlineAt)
            {
                confirmation.Status = "EXPIRED";
                _confirmationRepo.Update(confirmation);
                await _uow.CommitTransactionAsync();

                // Background service will handle cooldown notification on its next cycle
                return BuildTerminalStatus("EXPIRED", confirmation);
            }

            // === Mark this player's confirm flag ===
            var isPlayer1 = confirmation.Player1UserId == userId;
            if (isPlayer1)
                confirmation.Player1Confirmed = true;
            else
                confirmation.Player2Confirmed = true;

            _confirmationRepo.Update(confirmation);

            // === Check if both players have now confirmed ===
            if (!confirmation.Player1Confirmed || !confirmation.Player2Confirmed)
            {
                // Only one player confirmed so far
                await _uow.CommitTransactionAsync();

                var remaining = Math.Max(0, (int)(confirmation.ConfirmDeadlineAt - DateTime.UtcNow).TotalSeconds);

                // Notify both of partial confirmation progress
                await _notifier.NotifyMatchConfirmationUpdatedAsync(
                    confirmation.Player1UserId,
                    confirmation.Player2UserId,
                    new
                    {
                        confirmationId = confirmation.Id,
                        player1Confirmed = confirmation.Player1Confirmed,
                        player2Confirmed = confirmation.Player2Confirmed,
                        confirmDeadlineAt = confirmation.ConfirmDeadlineAt,
                        remainingSeconds = remaining,
                        serverNow = DateTime.UtcNow
                    });

                return new MatchmakingStatusDto
                {
                    Status = "MATCH_CONFIRMING",
                    ConfirmationId = confirmation.Id,
                    Player1Confirmed = confirmation.Player1Confirmed,
                    Player2Confirmed = confirmation.Player2Confirmed,
                    ConfirmDeadlineAt = confirmation.ConfirmDeadlineAt,
                    RemainingSeconds = remaining,
                    MeUserId = userId,
                    ServerNow = DateTime.UtcNow
                };
            }

            // === Both confirmed — create the official OnlineMatch ===

            // Race-condition guard: check if MatchId was set by a concurrent request
            if (confirmation.MatchId.HasValue)
            {
                await _uow.CommitTransactionAsync();
                return await BuildMatchedStatus(userId, confirmation);
            }

            // Generate scramble — load puzzle type for its code + scramble length
            var puzzleTypeId = confirmation.PuzzleTypeId;
            var puzzleType = await _puzzleTypeRepo.GetByIdAsync(puzzleTypeId)
                ?? throw new InvalidOperationException($"PuzzleType {puzzleTypeId} not found.");
            var matchId = Guid.NewGuid();
            var reservation = await _scramblePool.ReserveAsync("ONLINE_MATCH", puzzleTypeId,
                "ONLINE_MATCH", matchId, userId);
            var scramble = reservation.Sequence;

            // Load profiles to get their IDs
            var p1Profile = await _profileRepo.GetProfileAsync(confirmation.Player1UserId, puzzleTypeId)
                ?? throw new InvalidOperationException($"Player 1 profile not initialized for {puzzleTypeId}.");
            var p2Profile = await _profileRepo.GetProfileAsync(confirmation.Player2UserId, puzzleTypeId)
                ?? throw new InvalidOperationException($"Player 2 profile not initialized for {puzzleTypeId}.");

            // Create the OnlineMatch
            var setupDeadline = DateTime.UtcNow.AddMinutes(5);
            var expectedStateJson = reservation.ExpectedStateJson ?? System.Text.Json.JsonSerializer.Serialize(
                RubikCubeStateValidator.BuildExpectedCubeStateForScramble(scramble));

            var match = new OnlineMatch
            {
                Id = matchId,
                PuzzleTypeId = puzzleTypeId,
                ScramblePoolItemId = reservation.Id,
                Player1Id = confirmation.Player1UserId,
                Player2Id = confirmation.Player2UserId,
                Player1ProfileId = p1Profile.Id,
                Player2ProfileId = p2Profile.Id,
                ScrambleSequence = scramble,
                Player1ExpectedStateJson = expectedStateJson,
                Player2ExpectedStateJson = expectedStateJson,
                RoomToken = Guid.NewGuid().ToString("N"),
                QrSessionCode = GenerateQrCode(),
                StatusCode = OnlineMatchStatus.CREATED.ToString(),
                Phase = "ROOM_SETUP",
                SetupDeadlineAt = setupDeadline,
                CreatedAt = DateTime.UtcNow
            };
            await _matchRepo.AddAsync(match);
            await _scramblePool.MarkUsedAsync(reservation.Id, userId);

            // Update confirmation
            confirmation.Status = "CONFIRMED";
            confirmation.ConfirmedAt = DateTime.UtcNow;
            confirmation.MatchId = match.Id;
            _confirmationRepo.Update(confirmation);

            // Move both queue entries from CONFIRMING → MATCHED
            var p1Queue = await _queueRepo.GetConfirmingQueueAsync(confirmation.Player1UserId, puzzleTypeId);
            if (p1Queue != null)
            {
                p1Queue.StatusCode = MatchmakingQueueStatus.MATCHED.ToString();
                _queueRepo.Update(p1Queue);
            }

            var p2Queue = await _queueRepo.GetConfirmingQueueAsync(confirmation.Player2UserId, puzzleTypeId);
            if (p2Queue != null)
            {
                p2Queue.StatusCode = MatchmakingQueueStatus.MATCHED.ToString();
                _queueRepo.Update(p2Queue);
            }

            await _uow.CommitTransactionAsync();

            // Build per-player personalized payloads
            var p1Payload = new
            {
                status = "MATCHED",
                matchId = match.Id,
                roomToken = match.RoomToken,
                qrSessionCode = match.QrSessionCode,
                setupDeadlineAt = setupDeadline,
                meUserId = confirmation.Player1UserId,
                opponentUserId = confirmation.Player2UserId,
                scramble = match.ScrambleSequence,
                serverNow = DateTime.UtcNow
            };

            var p2Payload = new
            {
                status = "MATCHED",
                matchId = match.Id,
                roomToken = match.RoomToken,
                qrSessionCode = match.QrSessionCode,
                setupDeadlineAt = setupDeadline,
                meUserId = confirmation.Player2UserId,
                opponentUserId = confirmation.Player1UserId,
                scramble = match.ScrambleSequence,
                serverNow = DateTime.UtcNow
            };

            await _notifier.NotifyMatchConfirmedAsync(
                confirmation.Player1UserId,
                confirmation.Player2UserId,
                p1Payload, p2Payload);

            // Return result for the confirming user
            var myOpponentId = isPlayer1 ? confirmation.Player2UserId : confirmation.Player1UserId;
            return new MatchmakingStatusDto
            {
                Status = "MATCHED",
                MatchId = match.Id,
                MatchStatus = match.StatusCode,
                RoomToken = match.RoomToken,
                QrSessionCode = match.QrSessionCode,
                SetupDeadlineAt = setupDeadline,
                MeUserId = userId,
                OpponentUserId = myOpponentId,
                ServerNow = DateTime.UtcNow
            };
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }

    // ---- Helpers ----

    private static MatchmakingStatusDto BuildTerminalStatus(string status, OnlineMatchConfirmation confirmation)
        => new()
        {
            Status = status,
            ConfirmationId = confirmation.Id,
            ServerNow = DateTime.UtcNow
        };

    private async Task<MatchmakingStatusDto> BuildMatchedStatus(Guid userId, OnlineMatchConfirmation confirmation)
    {
        if (!confirmation.MatchId.HasValue)
            return BuildTerminalStatus("CONFIRMED", confirmation);

        var match = await _matchRepo.GetByIdAsync(confirmation.MatchId.Value);
        if (match == null)
            return BuildTerminalStatus("CONFIRMED", confirmation);

        var opponentId = confirmation.Player1UserId == userId
            ? confirmation.Player2UserId
            : confirmation.Player1UserId;

        return new MatchmakingStatusDto
        {
            Status = "MATCHED",
            MatchId = match.Id,
            MatchStatus = match.StatusCode,
            RoomToken = match.RoomToken,
            QrSessionCode = match.QrSessionCode,
            SetupDeadlineAt = match.SetupDeadlineAt,
            MeUserId = userId,
            OpponentUserId = opponentId,
            ServerNow = DateTime.UtcNow
        };
    }

    private static string GenerateQrCode()
        => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
}
