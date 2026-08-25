using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces.OnlineArena;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public class GetMatchRecoveryStateUseCase
{
    private readonly IOnlineMatchRepository _matchRepo;

    public GetMatchRecoveryStateUseCase(IOnlineMatchRepository matchRepo)
    {
        _matchRepo = matchRepo;
    }

    public async Task<OnlineMatchRecoveryStateDto> ExecuteAsync(Guid userId, Guid matchId)
    {
        var match = await _matchRepo.GetByIdWithPlayersAsync(matchId);
        if (match == null)
            throw new KeyNotFoundException("Match not found.");

        if (match.Player1Id != userId && match.Player2Id != userId)
            throw new UnauthorizedAccessException("Not a player in this match.");

        var isP1 = match.Player1Id == userId;
        var myResultStatus = isP1 ? match.Player1ResultStatus : match.Player2ResultStatus;
        var myFinishStatus = isP1 ? match.Player1FinishCheckStatus : match.Player2FinishCheckStatus;

        var isTerminal = OnlineArenaFlowHelpers.IsTerminal(match.StatusCode);
        var p1ChecklistPassed = OnlineArenaFlowHelpers.IsChecklistPassed(match, true);
        var p2ChecklistPassed = OnlineArenaFlowHelpers.IsChecklistPassed(match, false);

        // Compute ME recovery fields
        var canSubmit = match.StatusCode == OnlineMatchStatus.ONGOING.ToString()
                        && match.Phase == "SOLVING"
                        && myResultStatus == PlayerResultStatus.PENDING.ToString();

        var canStartFinish = myResultStatus == PlayerResultStatus.VALID.ToString()
                             && (myFinishStatus == "NOT_STARTED" || myFinishStatus == "FAILED")
                             && !isTerminal;

        var canWatch = myResultStatus != PlayerResultStatus.PENDING.ToString();

        var isInspectingActive = match.InspectionDeadlineAt.HasValue && DateTime.UtcNow < match.InspectionDeadlineAt.Value;

        // nextUiState: derived directly from match.Phase — backend is single source of truth
        var nextUiState = match.Phase switch
        {
            "ROOM_SETUP" or "WEBRTC_CONNECTING" or "MOBILE_TIMER_PAIRING" or "SCRAMBLE_CHECKING" => "SETUP",
            "COUNTDOWN" => "COUNTDOWN",
            "INSPECTION" or "SOLVING" or "FINISH_CHECKING" or "PENDING_EVIDENCE" =>
                myFinishStatus == "PASSED"
                ? "WAITING_OPPONENT"
                // DNF / DISCONNECTED players skip the finish check entirely — go straight to waiting.
                // Also covers NOT_REQUIRED finish status set by server when isDnf is true.
                : (myResultStatus == PlayerResultStatus.DNF.ToString()
                   || myResultStatus == PlayerResultStatus.DISCONNECTED.ToString()
                   || myFinishStatus == "NOT_REQUIRED"
                    ? "WAITING_OPPONENT"
                    : (myResultStatus == PlayerResultStatus.PENDING.ToString()
                        ? (isInspectingActive ? "INSPECTION" : "SOLVING")
                        : "FINISH_SCANNING")),
            "NEEDS_REVIEW" => "NEEDS_REVIEW",
            "COMPLETED" or "CANCELLED" => "COMPLETED",
            _ => "SETUP"
        };




        if (match.Phase is "ROOM_SETUP" or "WEBRTC_CONNECTING" or "MOBILE_TIMER_PAIRING" or "SCRAMBLE_CHECKING")
        {
            var myCamWeb = isP1
                ? match.Player1CameraReady && match.Player1WebRtcConnected && match.Player1TimerReady
                : match.Player2CameraReady && match.Player2WebRtcConnected && match.Player2TimerReady;
            var myScram = isP1 ? match.Player1ScrambleCheckStatus : match.Player2ScrambleCheckStatus;

            if (myScram != "PASSED")
                nextUiState = "SCRAMBLE_CHECK";
            else if (!myCamWeb)
                nextUiState = "SETUP";
        }

        // Override for terminal statuses regardless of phase
        if (match.StatusCode is nameof(OnlineMatchStatus.CANCELLED) or nameof(OnlineMatchStatus.COMPLETED))
            nextUiState = "COMPLETED";
        if (match.StatusCode == nameof(OnlineMatchStatus.NEEDS_REVIEW))
            nextUiState = "NEEDS_REVIEW";

        return new OnlineMatchRecoveryStateDto
        {
            MatchId = match.Id,
            StatusCode = match.StatusCode,
            Phase = match.Phase,
            QrSessionCode = match.QrSessionCode,
            SetupDeadlineAt = match.SetupDeadlineAt,
            CountdownEndsAt = match.CountdownEndsAt,
            ScrambleSequence = match.ScrambleSequence,
            InspectionDeadlineAt = match.InspectionDeadlineAt,
            SolveDeadlineAt = match.SolveDeadlineAt,
            Outcome = match.Outcome,
            WinnerId = match.WinnerId,
            Player1EloBefore = match.Player1EloBefore ?? match.Player1Profile?.EloStandard ?? 1000,
            Player2EloBefore = match.Player2EloBefore ?? match.Player2Profile?.EloStandard ?? 1000,
            Player1EloAfter = match.Player1EloAfter,
            Player2EloAfter = match.Player2EloAfter,
            ServerNow = DateTime.UtcNow,
            Player1 = new RecoveryPlayerStateDto
            {
                UserId = match.Player1Id,
                DisplayName = match.Player1?.DisplayName,
                ResultStatus = match.Player1ResultStatus,
                TimeMs = match.Player1TimeMs,
                FinishCheckStatus = match.Player1FinishCheckStatus,
                IsReady = match.Player1Ready,
                ChecklistPassed = p1ChecklistPassed,
                ScrambleCheckStatus = match.Player1ScrambleCheckStatus,
                CameraReady = match.Player1CameraReady,
                WebRtcConnected = match.Player1WebRtcConnected,
                RecordingStarted = match.Player1RecordingStarted,
                TimerReady = match.Player1TimerReady
            },
            Player2 = new RecoveryPlayerStateDto
            {
                UserId = match.Player2Id,
                DisplayName = match.Player2?.DisplayName,
                ResultStatus = match.Player2ResultStatus,
                TimeMs = match.Player2TimeMs,
                FinishCheckStatus = match.Player2FinishCheckStatus,
                IsReady = match.Player2Ready,
                ChecklistPassed = p2ChecklistPassed,
                ScrambleCheckStatus = match.Player2ScrambleCheckStatus,
                CameraReady = match.Player2CameraReady,
                WebRtcConnected = match.Player2WebRtcConnected,
                RecordingStarted = match.Player2RecordingStarted,
                TimerReady = match.Player2TimerReady
            },
            Me = new RecoveryMeStateDto
            {
                UserId = userId,
                CanSubmitTime = canSubmit,
                CanStartFinishCheck = canStartFinish,
                CanWatchOpponent = canWatch,
                NextUiState = nextUiState
            }
        };
    }
}
