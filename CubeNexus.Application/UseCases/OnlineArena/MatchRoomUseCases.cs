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

    public async Task<OnlineMatchStateDto> ExecuteAsync(Guid matchId, Guid userId)
    {
        var match = await RequireParticipantMatchAsync(matchId, userId);
        EnsureNotTerminal(match.StatusCode);

        if (match.Player1Id == userId)
            match.Player1CameraReady = true;
        else
            match.Player2CameraReady = true;

        await AutoReadyIfChecklistPassedAsync(match, userId, _matchRepo, _notifier, _uow);
        return OnlineArenaFlowHelpers.BuildMatchState(match, userId, false);
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

    // Legacy
    internal static MatchReadinessResponseDto BuildReadinessResponse(CubeNexus.Domain.Entities.OnlineMatch match, string message)
        => OnlineArenaFlowHelpers.BuildReadinessResponse(match, message);

    /// <summary>
    /// Shared event-driven auto-ready logic.
    /// Called after every checklist-changing action: camera, webrtc, timer, scramble.
    /// If checklist passes for this player, sets playerReady = true immediately.
    /// If both players become ready, transitions to COUNTDOWN immediately (event-driven, no polling).
    /// </summary>
    internal static async Task AutoReadyIfChecklistPassedAsync(
        CubeNexus.Domain.Entities.OnlineMatch match,
        Guid userId,
        IOnlineMatchRepository matchRepo,
        IOnlineArenaRealtimeNotifier notifier,
        IUnitOfWork uow)
    {
        var isP1 = match.Player1Id == userId;

        // Auto-set playerReady if this player's checklist passes
        if (OnlineArenaFlowHelpers.IsChecklistPassed(match, isP1))
        {
            if (isP1) match.Player1Ready = true;
            else match.Player2Ready = true;
        }

        if (match.Player1Ready && match.Player2Ready
            && match.StatusCode == nameof(CubeNexus.Domain.Enums.OnlineMatchStatus.CREATED))
        {
            // Both checklists complete → transition to COUNTDOWN immediately
            match.StatusCode = CubeNexus.Domain.Enums.OnlineMatchStatus.READY.ToString();
            match.Phase = "COUNTDOWN";
            match.CountdownEndsAt = DateTime.UtcNow.AddSeconds(5);
            matchRepo.Update(match);
            await uow.SaveChangesAsync();

            var countdownPayload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Both players ready. Countdown started.");
            await notifier.NotifyCountdownStartedAsync(match.Id, countdownPayload);
        }
        else
        {
            // Update phase and notify checklist progress
            match.Phase = OnlineArenaFlowHelpers.ComputePhase(match);
            matchRepo.Update(match);
            await uow.SaveChangesAsync();

            var checklistPayload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Checklist updated.");
            await notifier.NotifyChecklistUpdatedAsync(match.Id, checklistPayload);
        }
    }
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

    public async Task<OnlineMatchStateDto> ExecuteAsync(Guid matchId, Guid userId)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");

        // Idempotent: nếu đã đang COUNTDOWN/ONGOING rồi
        if (match.StatusCode is nameof(OnlineMatchStatus.READY) or nameof(OnlineMatchStatus.ONGOING))
            return OnlineArenaFlowHelpers.BuildMatchState(match, userId, false);

        if (match.StatusCode != nameof(OnlineMatchStatus.CREATED))
            throw new ConflictException($"Cannot mark ready when match is {match.StatusCode}.");

        var isPlayer1 = match.Player1Id == userId;

        // Kiểm tra checklistPassed trước khi cho phép playerReady
        if (!OnlineArenaFlowHelpers.IsChecklistPassed(match, isPlayer1))
            throw new InvalidOperationException("Checklist must be completed before marking ready (camera, WebRTC, recording, timer, scramble check).");

        if (isPlayer1)
            match.Player1Ready = true;
        else
            match.Player2Ready = true;

        // Nếu cả 2 playerReady → phase COUNTDOWN
        if (match.Player1Ready && match.Player2Ready)
        {
            match.StatusCode = OnlineMatchStatus.READY.ToString();
            match.Phase = "COUNTDOWN";
            match.CountdownEndsAt = DateTime.UtcNow.AddSeconds(5);
        }
        else
        {
            match.Phase = OnlineArenaFlowHelpers.ComputePhase(match);
        }

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        var payload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Player ready.");
        await _notifier.NotifyReadyStateUpdatedAsync(match.Id, payload);

        if (match.StatusCode == nameof(OnlineMatchStatus.READY))
        {
            var countdownPayload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Countdown started.");
            await _notifier.NotifyCountdownStartedAsync(match.Id, countdownPayload);
        }

        return OnlineArenaFlowHelpers.BuildMatchState(match, userId, false);
    }

    internal static bool AllReady(CubeNexus.Domain.Entities.OnlineMatch match)
        => OnlineArenaFlowHelpers.AllReady(match);
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

    /// <summary>
    /// Idempotent: nếu match đã ONGOING → return current state.
    /// Server tự start khi CountdownEndsAt hết (BackgroundService).
    /// Endpoint này dùng để get state hoặc trigger nếu BackgroundService chậm.
    /// </summary>
    public async Task<OnlineMatchStateDto> ExecuteAsync(Guid matchId, Guid userId)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            return OnlineArenaFlowHelpers.BuildMatchState(match, userId, false);

        // Idempotent: already ONGOING
        if (match.StatusCode == nameof(OnlineMatchStatus.ONGOING))
            return OnlineArenaFlowHelpers.BuildMatchState(match, userId, false);

        // READY + countdown ended → trigger start
        if (match.StatusCode == nameof(OnlineMatchStatus.READY)
            && match.CountdownEndsAt.HasValue
            && DateTime.UtcNow >= match.CountdownEndsAt.Value)
        {
            await TransitionToInspectionAsync(match);
        }
        else if (match.StatusCode != nameof(OnlineMatchStatus.READY))
        {
            throw new InvalidOperationException($"Match must be READY with countdown completed before start. Current status: {match.StatusCode}, phase: {match.Phase}.");
        }

        return OnlineArenaFlowHelpers.BuildMatchState(match, userId, false);
    }

    public async Task TransitionToInspectionAsync(CubeNexus.Domain.Entities.OnlineMatch match)
    {
        var now = DateTime.UtcNow;
        match.StatusCode = nameof(OnlineMatchStatus.ONGOING);
        match.Phase = "INSPECTION";
        match.StartedAt = now;
        match.ScrambleRevealedAt = now;
        match.InspectionDeadlineAt = now.AddSeconds(15);

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        var payload = OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Inspection started.");
        await _notifier.NotifyInspectionStartedAsync(match.Id, payload);
        await _notifier.NotifyScrambleRevealedAsync(match.Id, new
        {
            matchId = match.Id,
            scrambleSequence = match.ScrambleSequence,
            serverNow = now,
            inspectionDeadlineAt = match.InspectionDeadlineAt,
            phase = match.Phase
        });
    }
}

public class GetMatchDetailUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;

    public GetMatchDetailUseCase(IOnlineMatchRepository matchRepo)
    {
        _matchRepo = matchRepo;
    }

    public async Task<OnlineMatchStateDto> ExecuteAsync(Guid requestingUserId, Guid matchId, bool isAdmin)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");

        return OnlineArenaFlowHelpers.BuildMatchState(match, requestingUserId, isAdmin);
    }

    public async Task<OnlineMatchStateDto> ExecuteByRoomTokenAsync(Guid requestingUserId, string roomToken, bool isAdmin)
    {
        var match = await _matchRepo.GetByRoomTokenAsync(roomToken);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");

        return OnlineArenaFlowHelpers.BuildMatchState(match, requestingUserId, isAdmin);
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

    public async Task<OnlineMatchStateDto> ExecuteAsync(Guid requestingUserId, Guid matchId, bool isAdmin)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (!isAdmin && match.Player1Id != requestingUserId && match.Player2Id != requestingUserId)
            throw new UnauthorizedAccessException("Not allowed to reconcile this match.");

        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            return OnlineArenaFlowHelpers.BuildMatchState(match, requestingUserId, isAdmin);

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
            return OnlineArenaFlowHelpers.BuildMatchState(refreshed, requestingUserId, isAdmin);
        }

        if ((bothResultsSubmitted && requiresReview) || (finishDeadlineExpired && !bothFinishPassed))
        {
            match.StatusCode = OnlineMatchStatus.NEEDS_REVIEW.ToString();
            match.Phase = "NEEDS_REVIEW";
            match.Outcome = OnlineMatchOutcome.INCONCLUSIVE.ToString();
            match.ReviewReasonJson = OnlineArenaFlowHelpers.MergeReviewReason(match.ReviewReasonJson, new
            {
                code = finishDeadlineExpired ? "FINISH_VALIDATION_TIMEOUT" : "MATCH_RECONCILED_TO_REVIEW",
                at = DateTime.UtcNow
            });

            _matchRepo.Update(match);
            await _uow.SaveChangesAsync();

            await _notifier.NotifyMatchNeedsReviewAsync(match.Id,
                OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Match moved to review."));
        }

        return OnlineArenaFlowHelpers.BuildMatchState(match, requestingUserId, isAdmin);
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

    public async Task<OnlineMatchStateDto> ExecuteAsync(Guid requestingUserId, Guid matchId, bool isAdmin)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");
        if (!isAdmin && match.Player1Id != requestingUserId && match.Player2Id != requestingUserId)
            throw new UnauthorizedAccessException("Not allowed to mock this match.");
        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            return OnlineArenaFlowHelpers.BuildMatchState(match, requestingUserId, isAdmin);

        if (match.StatusCode is not nameof(OnlineMatchStatus.ONGOING) and not nameof(OnlineMatchStatus.PENDING_EVIDENCE))
            throw new InvalidOperationException("Mock finish pass is only allowed after the match has started.");

        if (match.Player1Id == requestingUserId) match.Player1FinishCheckStatus = "PASSED";
        else match.Player2FinishCheckStatus = "PASSED";

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        await _notifier.NotifyFinishCheckUpdatedAsync(match.Id,
            OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Mock finish pass applied."));

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

        return OnlineArenaFlowHelpers.BuildMatchState(match, requestingUserId, isAdmin);
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

    public async Task<CancelMatchResponseDto> ExecuteAsync(Guid matchId, Guid userId, bool isAdmin, string? reason = null)
    {
        var match = await _matchRepo.GetByIdAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");

        if (!isAdmin && match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not allowed to cancel this match.");

        if (OnlineArenaFlowHelpers.IsTerminal(match.StatusCode))
            throw new ConflictException($"Match is already terminal ({match.StatusCode}).");

        // Nếu cancel ở setup phase → không tính Elo
        var inSetup = match.StatusCode == nameof(OnlineMatchStatus.CREATED)
                      || match.StatusCode == nameof(OnlineMatchStatus.READY);

        match.StatusCode = OnlineMatchStatus.CANCELLED.ToString();
        match.Phase = "CANCELLED";
        match.Outcome = OnlineMatchOutcome.CANCELLED.ToString();
        match.CancelReason = reason ?? (inSetup ? "PLAYER_LEFT_SETUP" : "PLAYER_LEFT");
        match.EloChanged = false; // Setup cancel never changes Elo
        match.EndedAt = DateTime.UtcNow;

        _matchRepo.Update(match);
        await _uow.SaveChangesAsync();

        var response = new CancelMatchResponseDto
        {
            Message = "Match cancelled.",
            MatchId = match.Id,
            StatusCode = match.StatusCode
        };

        await _notifier.NotifyMatchCancelledAsync(match.Id,
            OnlineArenaFlowHelpers.BuildSignalRStatePayload(match, "Match cancelled."));
        return response;
    }
}
