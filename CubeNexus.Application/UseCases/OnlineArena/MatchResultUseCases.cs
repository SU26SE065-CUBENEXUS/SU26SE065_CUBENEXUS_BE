using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;
using CubeNexus.Domain.Services;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class SubmitOnlineMatchResultUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IMobileTimerSessionRepository _sessionRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly CompleteOnlineMatchUseCase _completeUseCase;

    public SubmitOnlineMatchResultUseCase(
        IOnlineMatchRepository matchRepo,
        IMobileTimerSessionRepository sessionRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        CompleteOnlineMatchUseCase completeUseCase)
    {
        _matchRepo = matchRepo;
        _sessionRepo = sessionRepo;
        _notifier = notifier;
        _uow = uow;
        _completeUseCase = completeUseCase;
    }

    public async Task<SubmitResultResponseDto> ExecuteAsync(Guid userId, Guid matchId, int? timeMs, bool isDnf)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");
        if (match.StatusCode != OnlineMatchStatus.ONGOING.ToString())
            throw new InvalidOperationException("Match must be ONGOING before submitting result.");
        if (!isDnf && (timeMs == null || timeMs <= 0))
            throw new ArgumentException("timeMs must be > 0 when isDnf is false.");
        if (!isDnf && timeMs > match.TimeLimitMs)
            throw new ArgumentException("timeMs exceeds match time limit. Submit as DNF if time limit was exceeded.");

        var session = await _sessionRepo.GetSessionAsync(matchId, userId);
        if (session == null || !session.IsActive)
            throw new InvalidOperationException("No active mobile timer session for this player.");

        var isPlayer1 = match.Player1Id == userId;
        var scrambleStatus = isPlayer1 ? match.Player1ScrambleCheckStatus : match.Player2ScrambleCheckStatus;
        if (scrambleStatus == "FAILED")
            throw new InvalidOperationException("Cannot submit VALID result when AI scramble check failed.");
        if (isPlayer1 && match.Player1ResultStatus != PlayerResultStatus.PENDING.ToString())
            throw new ConflictException("Player already submitted result.");
        if (!isPlayer1 && match.Player2ResultStatus != PlayerResultStatus.PENDING.ToString())
            throw new ConflictException("Player already submitted result.");

        if (isPlayer1)
        {
            match.Player1IsDnf = isDnf;
            match.Player1TimeMs = isDnf ? null : timeMs;
            match.Player1ResultStatus = isDnf ? PlayerResultStatus.DNF.ToString() : PlayerResultStatus.VALID.ToString();
            match.Player1FinishedAt = DateTime.UtcNow;
        }
        else
        {
            match.Player2IsDnf = isDnf;
            match.Player2TimeMs = isDnf ? null : timeMs;
            match.Player2ResultStatus = isDnf ? PlayerResultStatus.DNF.ToString() : PlayerResultStatus.VALID.ToString();
            match.Player2FinishedAt = DateTime.UtcNow;
        }

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        await _notifier.NotifyResultSubmittedAsync(match.Id, new
        {
            matchId = match.Id,
            playerId = userId,
            playerResultStatus = isDnf ? PlayerResultStatus.DNF.ToString() : PlayerResultStatus.VALID.ToString(),
            timeMs,
            isDnf,
            matchStatus = match.StatusCode,
            isMatchCompleted = false
        });

        if (match.Player1ResultStatus == PlayerResultStatus.PENDING.ToString()
            || match.Player2ResultStatus == PlayerResultStatus.PENDING.ToString())
        {
            return new SubmitResultResponseDto
            {
                Message = "Result submitted. Waiting for opponent.",
                MatchId = match.Id,
                MatchStatus = match.StatusCode,
                Outcome = match.Outcome,
                PlayerResultStatus = isDnf ? PlayerResultStatus.DNF.ToString() : PlayerResultStatus.VALID.ToString(),
                WinnerId = null,
                IsMatchCompleted = false
            };
        }

        match.StatusCode = OnlineMatchStatus.PENDING_EVIDENCE.ToString();
        match.VideoEvidenceUploadDeadlineAt = DateTime.UtcNow.AddMinutes(2);
        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        return new SubmitResultResponseDto
        {
            Message = "Both results submitted. Waiting for finish cube validation.",
            MatchId = match.Id,
            MatchStatus = match.StatusCode,
            Outcome = match.Outcome,
            Player1ResultStatus = match.Player1ResultStatus,
            Player2ResultStatus = match.Player2ResultStatus,
            Player1TimeMs = match.Player1TimeMs,
            Player2TimeMs = match.Player2TimeMs,
            WinnerId = null,
            IsMatchCompleted = false
        };
    }
}

public class CompleteOnlineMatchUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineProfileRepository _profileRepo;
    private readonly IEloHistoryRepository _eloHistoryRepo;
    private readonly IOnlineMatchVideoEvidenceRepository _videoEvidenceRepo;
    private readonly IFraudReportRepository _fraudReportRepo;
    private readonly IEloCalculator _eloCalc;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public CompleteOnlineMatchUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineProfileRepository profileRepo,
        IEloHistoryRepository eloHistoryRepo,
        IOnlineMatchVideoEvidenceRepository videoEvidenceRepo,
        IFraudReportRepository fraudReportRepo,
        IEloCalculator eloCalc,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _profileRepo = profileRepo;
        _eloHistoryRepo = eloHistoryRepo;
        _videoEvidenceRepo = videoEvidenceRepo;
        _fraudReportRepo = fraudReportRepo;
        _eloCalc = eloCalc;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task<SubmitResultResponseDto> ExecuteAsync(Guid matchId)
    {
        var existing = await _matchRepo.GetByIdAsync(matchId);
        if (existing == null)
            throw new KeyNotFoundException("Match not found.");
        if (OnlineArenaFlowHelpers.IsTerminal(existing.StatusCode))
        {
            return BuildCompletedResponse(existing);
        }
        if (existing.StatusCode is not nameof(OnlineMatchStatus.PENDING_EVIDENCE) and not nameof(OnlineMatchStatus.NEEDS_REVIEW))
            throw new InvalidOperationException("Only PENDING_EVIDENCE or NEEDS_REVIEW matches can be completed.");
        if (existing.Player1ResultStatus == PlayerResultStatus.PENDING.ToString()
            || existing.Player2ResultStatus == PlayerResultStatus.PENDING.ToString())
            throw new InvalidOperationException("Both results are required before completion.");

        var fraudReports = await _fraudReportRepo.GetByMatchAsync(existing.Id);
        var shouldReview =
            existing.Player1ScrambleCheckStatus != "PASSED"
            || existing.Player2ScrambleCheckStatus != "PASSED"
            || existing.Player1FinishCheckStatus != "PASSED"
            || existing.Player2FinishCheckStatus != "PASSED"
            || existing.Player1ScrambleCheckStatus == "FAILED"
            || existing.Player2ScrambleCheckStatus == "FAILED"
            || OnlineArenaFlowHelpers.HasOpenFraudReport(fraudReports)
            || existing.Player1ScrambleCheckStatus == "NEEDS_REVIEW"
            || existing.Player2ScrambleCheckStatus == "NEEDS_REVIEW"
            || existing.Player1FinishCheckStatus == "NEEDS_REVIEW"
            || existing.Player2FinishCheckStatus == "NEEDS_REVIEW";

        if (shouldReview)
        {
            existing.StatusCode = OnlineMatchStatus.NEEDS_REVIEW.ToString();
            existing.Outcome = OnlineMatchOutcome.INCONCLUSIVE.ToString();
            _matchRepo.Update(existing);
            await _uow.SaveChangesAsync();

            var reviewResponse = BuildCompletedResponse(existing);
            reviewResponse.Message = "Match moved to review.";
            await _notifier.NotifyMatchNeedsReviewAsync(existing.Id, reviewResponse);
            return reviewResponse;
        }

        await _uow.BeginTransactionAsync();
        try
        {
            var p1Profile = await _profileRepo.GetProfileAsync(existing.Player1Id, existing.PuzzleTypeId);
            var p2Profile = await _profileRepo.GetProfileAsync(existing.Player2Id, existing.PuzzleTypeId);
            if (p1Profile == null || p2Profile == null)
                throw new InvalidOperationException("Online profile not found for one or both players.");

            existing.Outcome = OnlineArenaFlowHelpers.DetermineOutcome(existing);
            existing.WinnerId = existing.Outcome switch
            {
                nameof(OnlineMatchOutcome.PLAYER1_WIN) => existing.Player1Id,
                nameof(OnlineMatchOutcome.PLAYER2_WIN) => existing.Player2Id,
                _ => null
            };
            existing.StatusCode = OnlineMatchStatus.COMPLETED.ToString();
            existing.EndedAt = DateTime.UtcNow;

            var player1Score = existing.Outcome == nameof(OnlineMatchOutcome.PLAYER1_WIN) ? 1.0m : existing.Outcome == nameof(OnlineMatchOutcome.DRAW) ? 0.5m : 0.0m;
            var player2Score = existing.Outcome == nameof(OnlineMatchOutcome.PLAYER2_WIN) ? 1.0m : existing.Outcome == nameof(OnlineMatchOutcome.DRAW) ? 0.5m : 0.0m;

            var (player1EloAfter, player2EloAfter, expected1, expected2) = _eloCalc.Calculate(
                p1Profile.Elo,
                p1Profile.KFactorCurrent,
                player1Score,
                p2Profile.Elo,
                p2Profile.KFactorCurrent,
                player2Score);

            existing.Player1EloBefore = p1Profile.Elo;
            existing.Player2EloBefore = p2Profile.Elo;
            existing.Player1EloAfter = player1EloAfter;
            existing.Player2EloAfter = player2EloAfter;

            await _eloHistoryRepo.AddAsync(CreateHistory(existing.Id, p1Profile, player1EloAfter, player1Score, expected1));
            await _eloHistoryRepo.AddAsync(CreateHistory(existing.Id, p2Profile, player2EloAfter, player2Score, expected2));

            UpdateProfile(p1Profile, player1EloAfter, player1Score);
            UpdateProfile(p2Profile, player2EloAfter, player2Score);

            _profileRepo.Update(p1Profile);
            _profileRepo.Update(p2Profile);
            _matchRepo.Update(existing);

            await _uow.CommitTransactionAsync();

            var response = BuildCompletedResponse(existing);
            response.Message = "Match completed.";
            await _notifier.NotifyMatchCompletedAsync(existing.Id, response);
            return response;
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }

    private static EloHistory CreateHistory(Guid matchId, OnlineProfile profile, int eloAfter, decimal actualScore, decimal expectedScore)
        => new()
        {
            Id = Guid.NewGuid(),
            OnlineProfileId = profile.Id,
            MatchId = matchId,
            EloBefore = profile.Elo,
            EloAfter = eloAfter,
            Delta = eloAfter - profile.Elo,
            KFactorUsed = profile.KFactorCurrent,
            ActualScore = actualScore,
            ExpectedScore = expectedScore,
            IsPlacementMatch = !profile.IsPlacementComplete,
            ChangedAt = DateTime.UtcNow
        };

    private static void UpdateProfile(OnlineProfile profile, int newElo, decimal score)
    {
        profile.Elo = newElo;
        profile.PeakElo = Math.Max(profile.PeakElo, newElo);

        if (score == 1.0m) profile.TotalWins++;
        else if (score == 0.0m) profile.TotalLosses++;
        else profile.TotalDraws++;

        if (!profile.IsPlacementComplete)
        {
            profile.PlacementMatchesDone++;
            if (profile.PlacementMatchesDone >= 5)
            {
                profile.IsPlacementComplete = true;
                profile.PlacementCompletedAt = DateTime.UtcNow;
                profile.KFactorCurrent = 20;
            }
        }

        profile.UpdatedAt = DateTime.UtcNow;
    }

    private static SubmitResultResponseDto BuildCompletedResponse(OnlineMatch match)
        => new()
        {
            Message = "Match completed.",
            MatchId = match.Id,
            MatchStatus = match.StatusCode,
            Outcome = match.Outcome,
            WinnerId = match.WinnerId,
            Player1ResultStatus = match.Player1ResultStatus,
            Player2ResultStatus = match.Player2ResultStatus,
            Player1TimeMs = match.Player1TimeMs,
            Player2TimeMs = match.Player2TimeMs,
            Player1EloBefore = match.Player1EloBefore,
            Player1EloAfter = match.Player1EloAfter,
            Player2EloBefore = match.Player2EloBefore,
            Player2EloAfter = match.Player2EloAfter,
            IsMatchCompleted = match.StatusCode == nameof(OnlineMatchStatus.COMPLETED)
        };
}
