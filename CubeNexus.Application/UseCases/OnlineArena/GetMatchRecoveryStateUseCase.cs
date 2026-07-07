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
        var match = await _matchRepo.GetByIdAsync(matchId);
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

        // nextUiState: derived directly from match.Phase — backend is single source of truth
        var nextUiState = match.Phase switch
        {
            "ROOM_SETUP" or "WEBRTC_CONNECTING" or "MOBILE_TIMER_PAIRING" or "SCRAMBLE_CHECKING" => "SETUP",
            "COUNTDOWN" => "COUNTDOWN",
            "INSPECTION" => "INSPECTION",
            "SOLVING" => myResultStatus == PlayerResultStatus.PENDING.ToString() ? "SOLVING"
                        : (myResultStatus == PlayerResultStatus.VALID.ToString() && myFinishStatus != "PASSED") ? "FINISH_SCANNING"
                        : "WAITING_OPPONENT",
            "FINISH_CHECKING" => "FINISH_SCANNING",
            "PENDING_EVIDENCE" => "WAITING_OPPONENT",
            "NEEDS_REVIEW" => "NEEDS_REVIEW",
            "COMPLETED" or "CANCELLED" => "COMPLETED",
            _ => "SETUP"
        };

        // For ROOM_SETUP: refine between SETUP and SCRAMBLE_CHECK based on per-player progress
        if (match.Phase is "ROOM_SETUP" or "WEBRTC_CONNECTING" or "MOBILE_TIMER_PAIRING" or "SCRAMBLE_CHECKING")
        {
            var myChecklist = isP1 ? p1ChecklistPassed : p2ChecklistPassed;
            var myCamWeb = isP1
                ? match.Player1CameraReady && match.Player1WebRtcConnected && match.Player1TimerReady
                : match.Player2CameraReady && match.Player2WebRtcConnected && match.Player2TimerReady;
            var myScram = isP1 ? match.Player1ScrambleCheckStatus : match.Player2ScrambleCheckStatus;

            if (myCamWeb && myScram != "PASSED")
                nextUiState = "SCRAMBLE_CHECK";
            else if (!myChecklist)
                nextUiState = "SETUP";
        }

        // Override for terminal statuses regardless of phase
        if (match.StatusCode == nameof(OnlineMatchStatus.CANCELLED))
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
            ScrambleSequence = isP1 ? match.Player1ScrambleSequence : match.Player2ScrambleSequence,
            InspectionDeadlineAt = match.InspectionDeadlineAt,
            SolveDeadlineAt = match.SolveDeadlineAt,
            Outcome = match.Outcome,
            WinnerId = match.WinnerId,
            Player1EloBefore = match.Player1EloBefore,
            Player2EloBefore = match.Player2EloBefore,
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
