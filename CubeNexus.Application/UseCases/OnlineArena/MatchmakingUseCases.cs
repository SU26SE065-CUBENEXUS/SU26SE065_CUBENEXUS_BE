using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class FindOnlineMatchUseCase
{
    private readonly IMatchmakingQueueRepository _queueRepo;
    private readonly IOnlineProfileRepository _profileRepo;
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly IScrambleGeneratorService _scrambleGenerator;
    private readonly CubeNexus.Application.Interfaces.Repositories.IPuzzleTypeRepository _puzzleTypeRepo;

    public FindOnlineMatchUseCase(
        IMatchmakingQueueRepository queueRepo,
        IOnlineProfileRepository profileRepo,
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        IScrambleGeneratorService scrambleGenerator,
        CubeNexus.Application.Interfaces.Repositories.IPuzzleTypeRepository puzzleTypeRepo)
    {
        _queueRepo = queueRepo;
        _profileRepo = profileRepo;
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
        _scrambleGenerator = scrambleGenerator;
        _puzzleTypeRepo = puzzleTypeRepo;
    }

    public async Task<MatchmakingStatusDto> ExecuteAsync(Guid userId, Guid puzzleTypeId)
    {
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
                OpponentUserId = activeMatch.Player1Id == userId ? activeMatch.Player2Id : activeMatch.Player1Id
            };
        }

        var profile = await _profileRepo.GetProfileAsync(userId, puzzleTypeId);
        if (profile == null)
            throw new InvalidOperationException("Online profile not initialized for this puzzle type.");

        var queued = await _queueRepo.GetQueuedQueueAsync(userId, puzzleTypeId);
        if (queued != null)
        {
            return new MatchmakingStatusDto
            {
                Status = "QUEUED",
                QueueId = queued.Id
            };
        }

        await _uow.BeginTransactionAsync();
        try
        {
            var opponentQueue = await _queueRepo.FindMatchForUpdateAsync(puzzleTypeId, userId, profile.Elo, 200);
            if (opponentQueue == null)
            {
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
                    userId
                });

                return new MatchmakingStatusDto
                {
                    Status = "QUEUED",
                    QueueId = newQueue.Id
                };
            }

            opponentQueue.StatusCode = MatchmakingQueueStatus.MATCHED.ToString();
            _queueRepo.Update(opponentQueue);
            var puzzleType = await _puzzleTypeRepo.GetByIdAsync(puzzleTypeId)
                ?? throw new InvalidOperationException("Puzzle type not found.");
            var player1Scramble = _scrambleGenerator.GenerateScramble(puzzleType.Code, puzzleType.ScrambleLength);
            var player2Scramble = _scrambleGenerator.GenerateScramble(puzzleType.Code, puzzleType.ScrambleLength);

            var newMatch = new OnlineMatch
            {
                Id = Guid.NewGuid(),
                PuzzleTypeId = puzzleTypeId,
                ScrambleSequence = player1Scramble,
                Player1ScrambleSequence = player1Scramble,
                Player2ScrambleSequence = player2Scramble,
                Player1ExpectedStateJson = System.Text.Json.JsonSerializer.Serialize(RubikCubeStateValidator.BuildExpectedCubeStateForScramble(player1Scramble)),
                Player2ExpectedStateJson = System.Text.Json.JsonSerializer.Serialize(RubikCubeStateValidator.BuildExpectedCubeStateForScramble(player2Scramble)),
                Player1Id = opponentQueue.UserId,
                Player2Id = userId,
                Player1ProfileId = opponentQueue.OnlineProfileId,
                Player2ProfileId = profile.Id,
                StatusCode = OnlineMatchStatus.CREATED.ToString(),
                RoomToken = Guid.NewGuid().ToString("N"),
                QrSessionCode = Guid.NewGuid().ToString("N"),
                Player1EloBefore = opponentQueue.OnlineProfile.Elo,
                Player2EloBefore = profile.Elo,
                CreatedAt = DateTime.UtcNow
            };

            await _matchRepo.AddAsync(newMatch);
            await _uow.CommitTransactionAsync();

            await _notifier.NotifyMatchmakingFoundAsync(
                newMatch.Player1Id,
                newMatch.Player2Id,
                new
                {
                    player1Payload = new
                    {
                        matchId = newMatch.Id,
                        matchStatus = newMatch.StatusCode,
                        roomToken = newMatch.RoomToken,
                        qrSessionCode = newMatch.QrSessionCode,
                        puzzleTypeId = newMatch.PuzzleTypeId,
                        meUserId = newMatch.Player1Id,
                        opponentUserId = newMatch.Player2Id
                    },
                    player2Payload = new
                    {
                        matchId = newMatch.Id,
                        matchStatus = newMatch.StatusCode,
                        roomToken = newMatch.RoomToken,
                        qrSessionCode = newMatch.QrSessionCode,
                        puzzleTypeId = newMatch.PuzzleTypeId,
                        meUserId = newMatch.Player2Id,
                        opponentUserId = newMatch.Player1Id
                    }
                });

            return new MatchmakingStatusDto
            {
                Status = "MATCHED",
                MatchId = newMatch.Id,
                MatchStatus = newMatch.StatusCode,
                RoomToken = newMatch.RoomToken,
                QrSessionCode = newMatch.QrSessionCode,
                MeUserId = userId,
                OpponentUserId = newMatch.Player1Id == userId ? newMatch.Player2Id : newMatch.Player1Id
            };
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }
}

public class CancelMatchmakingUseCase
{
    private readonly IMatchmakingQueueRepository _queueRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public CancelMatchmakingUseCase(
        IMatchmakingQueueRepository queueRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _queueRepo = queueRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task ExecuteAsync(Guid userId, Guid puzzleTypeId)
    {
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
            puzzleTypeId
        });
    }
}

public class GetMatchmakingStatusUseCase
{
    private readonly IMatchmakingQueueRepository _queueRepo;
    private readonly IOnlineMatchRepository _matchRepo;

    public GetMatchmakingStatusUseCase(
        IMatchmakingQueueRepository queueRepo,
        IOnlineMatchRepository matchRepo)
    {
        _queueRepo = queueRepo;
        _matchRepo = matchRepo;
    }

    public async Task<MatchmakingStatusDto> ExecuteAsync(Guid userId, Guid puzzleTypeId)
    {
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
                OpponentUserId = activeMatch.Player1Id == userId ? activeMatch.Player2Id : activeMatch.Player1Id
            };
        }

        var queued = await _queueRepo.GetQueuedQueueAsync(userId, puzzleTypeId);
        if (queued != null)
        {
            return new MatchmakingStatusDto
            {
                Status = "QUEUED",
                QueueId = queued.Id
            };
        }

        return new MatchmakingStatusDto
        {
            Status = "IDLE"
        };
    }
}
