using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class MarkCameraReadyUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public MarkCameraReadyUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task<MatchReadinessResponseDto> ExecuteAsync(Guid matchId, Guid userId)
    {
        var match = await RequireParticipantMatchAsync(matchId, userId);
        EnsureNotTerminal(match.StatusCode);

        if (match.Player1Id == userId)
            match.Player1CameraReady = true;
        else
            match.Player2CameraReady = true;

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        var response = BuildReadinessResponse(match, "Camera ready.");
        await _notifier.NotifyCameraReadyUpdatedAsync(matchId, response);
        await _notifier.NotifyReadyStateUpdatedAsync(matchId, response);
        return response;
    }

    private async Task<CubeNexus.Domain.Entities.OnlineMatch> RequireParticipantMatchAsync(Guid matchId, Guid userId)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");
        return match;
    }

    private static void EnsureNotTerminal(string statusCode)
    {
        if (OnlineArenaFlowHelpers.IsTerminal(statusCode))
            throw new ConflictException($"Match is already terminal ({statusCode}).");
    }

    internal static MatchReadinessResponseDto BuildReadinessResponse(CubeNexus.Domain.Entities.OnlineMatch match, string message)
        => OnlineArenaFlowHelpers.BuildReadinessResponse(match, message);
}

public class MarkPlayerReadyUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public MarkPlayerReadyUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task<MatchReadinessResponseDto> ExecuteAsync(Guid matchId, Guid userId)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");
        if (match.StatusCode == OnlineMatchStatus.ONGOING.ToString())
            throw new InvalidOperationException("Match already started.");

        if (match.StatusCode == OnlineMatchStatus.READY.ToString())
        {
            return OnlineArenaFlowHelpers.BuildReadinessResponse(match, "Match ready.");
        }

        if (match.StatusCode != OnlineMatchStatus.CREATED.ToString())
            throw new ConflictException($"Cannot mark ready when match is {match.StatusCode}.");

        if (match.Player1Id == userId)
        {
            if (!match.Player1CameraReady)
                throw new InvalidOperationException("Camera is not ready.");
            if (!match.Player1WebRtcConnected)
                throw new InvalidOperationException("WebRTC is not connected.");
            if (!match.Player1RecordingStarted)
                throw new InvalidOperationException("Video recording has not started.");
            if (!match.Player1TimerReady)
                throw new InvalidOperationException("Mobile timer is not connected.");
            if (match.Player1ScrambleCheckStatus != "PASSED")
                throw new InvalidOperationException("Scramble validation has not passed.");
            match.Player1Ready = true;
        }
        else
        {
            if (!match.Player2CameraReady)
                throw new InvalidOperationException("Camera is not ready.");
            if (!match.Player2WebRtcConnected)
                throw new InvalidOperationException("WebRTC is not connected.");
            if (!match.Player2RecordingStarted)
                throw new InvalidOperationException("Video recording has not started.");
            if (!match.Player2TimerReady)
                throw new InvalidOperationException("Mobile timer is not connected.");
            if (match.Player2ScrambleCheckStatus != "PASSED")
                throw new InvalidOperationException("Scramble validation has not passed.");
            match.Player2Ready = true;
        }

        if (AllReady(match))
        {
            match.StatusCode = OnlineMatchStatus.READY.ToString();
        }

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        var response = MarkCameraReadyUseCase.BuildReadinessResponse(match, "Player ready.");
        await _notifier.NotifyReadyStateUpdatedAsync(match.Id, response);

        if (match.StatusCode == OnlineMatchStatus.READY.ToString())
        {
            var readyResponse = MarkCameraReadyUseCase.BuildReadinessResponse(match, "Match ready.");
            await _notifier.NotifyMatchReadyAsync(match.Id, readyResponse);
            return readyResponse;
        }

        return response;
    }

    internal static bool AllReady(CubeNexus.Domain.Entities.OnlineMatch match)
        => match.Player1CameraReady
        && match.Player2CameraReady
        && match.Player1WebRtcConnected
        && match.Player2WebRtcConnected
        && match.Player1RecordingStarted
        && match.Player2RecordingStarted
        && match.Player1TimerReady
        && match.Player2TimerReady
        && match.Player1Ready
        && match.Player2Ready
        && match.Player1ScrambleCheckStatus == "PASSED"
        && match.Player2ScrambleCheckStatus == "PASSED";
}

public class StartOnlineMatchUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public StartOnlineMatchUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task<StartMatchResponseDto> ExecuteAsync(Guid matchId, Guid userId)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Cannot start match in status {match.StatusCode}.");

        if (match.StatusCode == OnlineMatchStatus.ONGOING.ToString())
        {
            return BuildStartResponse(match, userId);
        }

        if (match.StatusCode != OnlineMatchStatus.READY.ToString())
            throw new InvalidOperationException("Match must be READY before start.");
        if (!MarkPlayerReadyUseCase.AllReady(match))
            throw new InvalidOperationException("Both players must enable camera, connect WebRTC, start recording, connect timer, pass scramble validation, and be ready before starting.");

        match.StatusCode = OnlineMatchStatus.ONGOING.ToString();
        match.StartedAt = DateTime.UtcNow;
        match.ScrambleRevealedAt = match.StartedAt;

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        var response = BuildStartResponse(match, userId);
        await _notifier.NotifyScrambleRevealedAsync(match.Id, response);
        return response;
    }

    private static StartMatchResponseDto BuildStartResponse(CubeNexus.Domain.Entities.OnlineMatch match, Guid userId)
        => new()
        {
            Message = "Match started.",
            MatchId = match.Id,
            StatusCode = match.StatusCode,
            ScrambleSequence = match.Player1Id == userId ? match.Player1ScrambleSequence ?? match.ScrambleSequence : match.Player2ScrambleSequence ?? match.ScrambleSequence,
            PlayerScrambleSequence = match.Player1Id == userId ? match.Player1ScrambleSequence ?? match.ScrambleSequence : match.Player2ScrambleSequence ?? match.ScrambleSequence,
            StartedAt = match.StartedAt,
            ScrambleRevealedAt = match.ScrambleRevealedAt,
            TimeLimitMs = match.TimeLimitMs
        };
}

public class GetMatchDetailUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;

    public GetMatchDetailUseCase(IOnlineMatchRepository matchRepo)
    {
        _matchRepo = matchRepo;
    }

    public async Task<OnlineMatchDetailDto> ExecuteAsync(Guid requestingUserId, Guid matchId, bool isAdmin)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");

        return OnlineArenaFlowHelpers.BuildMatchDetail(match, requestingUserId, isAdmin);
    }

    public async Task<OnlineMatchDetailDto> ExecuteByRoomTokenAsync(Guid requestingUserId, string roomToken, bool isAdmin)
    {
        var match = await _matchRepo.GetByRoomTokenAsync(roomToken);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");

        return OnlineArenaFlowHelpers.BuildMatchDetail(match, requestingUserId, isAdmin);
    }
}

public class ReconcileOnlineMatchStatusUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly CompleteOnlineMatchUseCase _completeUseCase;

    public ReconcileOnlineMatchStatusUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        CompleteOnlineMatchUseCase completeUseCase)
    {
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
        _completeUseCase = completeUseCase;
    }

    public async Task<OnlineMatchDetailDto> ExecuteAsync(Guid requestingUserId, Guid matchId, bool isAdmin)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (!isAdmin && match.Player1Id != requestingUserId && match.Player2Id != requestingUserId)
            throw new UnauthorizedAccessException("Not allowed to reconcile this match.");

        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            return OnlineArenaFlowHelpers.BuildMatchDetail(match, requestingUserId, isAdmin);

        var bothResultsSubmitted =
            match.Player1ResultStatus != PlayerResultStatus.PENDING.ToString()
            && match.Player2ResultStatus != PlayerResultStatus.PENDING.ToString();
        var bothFinishPassed =
            match.Player1FinishCheckStatus == "PASSED"
            && match.Player2FinishCheckStatus == "PASSED";
        var requiresReview =
            match.Player1ScrambleCheckStatus is "FAILED" or "NEEDS_REVIEW"
            || match.Player2ScrambleCheckStatus is "FAILED" or "NEEDS_REVIEW"
            || match.Player1FinishCheckStatus is "FAILED" or "NEEDS_REVIEW"
            || match.Player2FinishCheckStatus is "FAILED" or "NEEDS_REVIEW";
        var finishDeadlineExpired =
            match.StatusCode == nameof(OnlineMatchStatus.PENDING_EVIDENCE)
            && match.VideoEvidenceUploadDeadlineAt.HasValue
            && DateTime.UtcNow >= match.VideoEvidenceUploadDeadlineAt.Value;

        if (bothResultsSubmitted && bothFinishPassed
            && match.StatusCode is nameof(OnlineMatchStatus.PENDING_EVIDENCE) or nameof(OnlineMatchStatus.NEEDS_REVIEW))
        {
            await _completeUseCase.ExecuteAsync(match.Id);
            var refreshed = await _matchRepo.GetByIdAsync(matchId) ?? match;
            return OnlineArenaFlowHelpers.BuildMatchDetail(refreshed, requestingUserId, isAdmin);
        }

        if ((bothResultsSubmitted && requiresReview) || (finishDeadlineExpired && !bothFinishPassed))
        {
            match.StatusCode = OnlineMatchStatus.NEEDS_REVIEW.ToString();
            match.Outcome = OnlineMatchOutcome.INCONCLUSIVE.ToString();
            match.ReviewReasonJson = OnlineArenaFlowHelpers.MergeReviewReason(match.ReviewReasonJson, new
            {
                code = finishDeadlineExpired ? "FINISH_VALIDATION_TIMEOUT" : "MATCH_RECONCILED_TO_REVIEW",
                at = DateTime.UtcNow
            });

            _matchRepo.Update(match);
            await _uow.SaveChangesAsync();

            var detail = OnlineArenaFlowHelpers.BuildMatchDetail(match, requestingUserId, isAdmin);
            await _notifier.NotifyMatchNeedsReviewAsync(match.Id, new
            {
                message = "Match moved to review by reconcile.",
                matchId = match.Id,
                matchStatus = match.StatusCode,
                player1FinishCheckStatus = match.Player1FinishCheckStatus,
                player2FinishCheckStatus = match.Player2FinishCheckStatus,
                player1ResultStatus = match.Player1ResultStatus,
                player2ResultStatus = match.Player2ResultStatus
            });
            return detail;
        }

        return OnlineArenaFlowHelpers.BuildMatchDetail(match, requestingUserId, isAdmin);
    }
}

public class MockOnlineMatchFinishPassUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;
    private readonly CompleteOnlineMatchUseCase _completeUseCase;

    public MockOnlineMatchFinishPassUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow,
        CompleteOnlineMatchUseCase completeUseCase)
    {
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
        _completeUseCase = completeUseCase;
    }

    public async Task<OnlineMatchDetailDto> ExecuteAsync(Guid requestingUserId, Guid matchId, bool isAdmin)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (!isAdmin && match.Player1Id != requestingUserId && match.Player2Id != requestingUserId)
            throw new UnauthorizedAccessException("Not allowed to mock this match.");
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            return OnlineArenaFlowHelpers.BuildMatchDetail(match, requestingUserId, isAdmin);

        if (match.StatusCode is not nameof(OnlineMatchStatus.ONGOING) and not nameof(OnlineMatchStatus.PENDING_EVIDENCE))
            throw new InvalidOperationException("Mock finish pass is only allowed after the match has started.");

        if (match.Player1Id == requestingUserId) match.Player1FinishCheckStatus = "PASSED";
        else match.Player2FinishCheckStatus = "PASSED";

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        await _notifier.NotifyFinishCheckUpdatedAsync(match.Id, new
        {
            message = "Mock finish pass applied.",
            matchId = match.Id,
            playerId = requestingUserId,
            player1FinishCheckStatus = match.Player1FinishCheckStatus,
            player2FinishCheckStatus = match.Player2FinishCheckStatus,
            matchStatus = match.StatusCode,
            source = "DEV_MOCK"
        });

        var bothResultsSubmitted =
            match.Player1ResultStatus != PlayerResultStatus.PENDING.ToString()
            && match.Player2ResultStatus != PlayerResultStatus.PENDING.ToString();
        var bothFinishPassed =
            match.Player1FinishCheckStatus == "PASSED"
            && match.Player2FinishCheckStatus == "PASSED";

        if (bothResultsSubmitted && bothFinishPassed
            && match.StatusCode is nameof(OnlineMatchStatus.PENDING_EVIDENCE) or nameof(OnlineMatchStatus.NEEDS_REVIEW))
        {
            await _completeUseCase.ExecuteAsync(match.Id);
            match = await _matchRepo.GetByIdAsync(match.Id) ?? match;
        }

        return OnlineArenaFlowHelpers.BuildMatchDetail(match, requestingUserId, isAdmin);
    }
}

public class CancelActiveMatchUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;
    private readonly IOnlineArenaRealtimeNotifier _notifier;
    private readonly IUnitOfWork _uow;

    public CancelActiveMatchUseCase(
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        _matchRepo = matchRepo;
        _notifier = notifier;
        _uow = uow;
    }

    public async Task<CancelMatchResponseDto> ExecuteAsync(Guid matchId, Guid userId, bool isAdmin)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");

        if (!isAdmin && match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not allowed to cancel this match.");

        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");

        match.StatusCode = OnlineMatchStatus.CANCELLED.ToString();
        match.Outcome = OnlineMatchOutcome.CANCELLED.ToString();
        match.EndedAt = DateTime.UtcNow;

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        var response = new CancelMatchResponseDto
        {
            Message = "Match cancelled.",
            MatchId = match.Id,
            StatusCode = match.StatusCode
        };

        await _notifier.NotifyMatchCancelledAsync(match.Id, response);
        return response;
    }
}
