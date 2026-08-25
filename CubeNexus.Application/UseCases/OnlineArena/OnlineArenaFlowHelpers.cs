using System.Text.Json;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

public static class OnlineArenaFlowHelpers
{
    // ===================================================================
    // Terminal / Active checks
    // ===================================================================

    public static bool IsTerminal(string statusCode)
        => statusCode is nameof(OnlineMatchStatus.COMPLETED)
            or nameof(OnlineMatchStatus.CANCELLED)
            or nameof(OnlineMatchStatus.DRAW);

    // ===================================================================
    // Checklist & Ready helpers
    // ===================================================================

    /// <summary>
    /// checklistPassed: camera + webRtc + timer + scrambleCheck == PASSED.
    /// RecordingStarted is NOT part of ROOM_SETUP checklist — it belongs to COUNTDOWN.
    /// KHÔNG bao gồm playerReady (auto-set khi checklist hoàn thành).
    /// </summary>
    public static bool IsChecklistPassed(OnlineMatch match, bool isPlayer1)
        => isPlayer1
            ? match.Player1WebRtcConnected
              && match.Player1TimerReady
              && match.Player1ScrambleCheckStatus == "PASSED"
            : match.Player2WebRtcConnected
              && match.Player2TimerReady
              && match.Player2ScrambleCheckStatus == "PASSED";

    /// <summary>
    /// Gate for COUNTDOWN → INSPECTION: both players must have started video recording.
    /// </summary>
    public static bool BothRecordingStarted(OnlineMatch match)
        => match.Player1RecordingStarted && match.Player2RecordingStarted;

    public static bool BothChecklistPassed(OnlineMatch match)
        => IsChecklistPassed(match, true) && IsChecklistPassed(match, false);

    /// <summary>
    /// AllReady: cả hai checklistPassed (playerReady được auto-set đồng thời).
    /// Không còn yêu cầu bấm nút Ready thủ công.
    /// </summary>
    public static bool AllReady(OnlineMatch match)
        => BothChecklistPassed(match);

    // ===================================================================
    // Phase computation (computed from stored state + deadlines)
    // ===================================================================

    /// <summary>
    /// Tính phase hiện tại của match từ trạng thái lưu trữ.
    /// Phase được lưu trực tiếp trong match.Phase, hàm này dùng để validate/override.
    /// </summary>
    public static string ComputePhase(OnlineMatch match)
    {
        // Terminal states
        if (match.StatusCode == nameof(OnlineMatchStatus.CANCELLED)) return "CANCELLED";
        if (match.StatusCode == nameof(OnlineMatchStatus.COMPLETED)) return "COMPLETED";
        if (match.StatusCode == nameof(OnlineMatchStatus.DRAW)) return "COMPLETED";
        if (match.StatusCode == nameof(OnlineMatchStatus.NEEDS_REVIEW)) return "NEEDS_REVIEW";
        if (match.StatusCode == nameof(OnlineMatchStatus.PENDING_EVIDENCE)) return "PENDING_EVIDENCE";

        // ONGOING states — kiểm tra phase con
        if (match.StatusCode == nameof(OnlineMatchStatus.ONGOING))
        {
            // Finish checking phase
            if (match.FinishCheckDeadlineAt.HasValue
                && (match.Player1ResultStatus != "PENDING" || match.Player2ResultStatus != "PENDING"))
                return "FINISH_CHECKING";

            // Solving phase (started, timer running)
            if (match.SolveDeadlineAt.HasValue)
                return "SOLVING";

            // Inspection phase
            if (match.InspectionDeadlineAt.HasValue)
                return "INSPECTION";

            return "SOLVING"; // fallback
        }

        // READY — countdown
        if (match.StatusCode == nameof(OnlineMatchStatus.READY))
            return "COUNTDOWN";

        // CREATED states — xác định bước setup
        // Khi BothChecklistPassed → auto-transition sang COUNTDOWN (event-driven, không cần WAITING_READY)
        if (match.StatusCode == nameof(OnlineMatchStatus.CREATED))
        {
            // Xác định player nào còn thiếu gì (Recording KHÔNG nằm trong setup checklist)
            var p1Checked = match.Player1WebRtcConnected
                            && match.Player1TimerReady;
            var p2Checked = match.Player2WebRtcConnected
                            && match.Player2TimerReady;

            // Nếu ai đó thiếu timer
            if (!match.Player1TimerReady || !match.Player2TimerReady)
                return "MOBILE_TIMER_PAIRING";

            // Nếu ai đó thiếu WebRTC
            if (!match.Player1WebRtcConnected || !match.Player2WebRtcConnected)
                return "WEBRTC_CONNECTING";

            // Đang check scramble
            return "SCRAMBLE_CHECKING";
        }

        return match.Phase; // fallback về stored value
    }

    // ===================================================================
    // Cooldown calculation
    // ===================================================================

    /// <summary>
    /// Tính cooldown duration dựa trên số lần timeout.
    /// Rule: 1→60s, 2→2min, 3→5min, 4+→10min.
    /// </summary>
    public static TimeSpan GetCooldownDuration(int timeoutCount)
        => timeoutCount switch
        {
            1 => TimeSpan.FromSeconds(60),
            2 => TimeSpan.FromMinutes(2),
            3 => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(10)
        };

    // ===================================================================
    // DTO builders
    // ===================================================================

    public static OnlineMatchStateDto BuildMatchState(OnlineMatch match, Guid requestingUserId, bool isAdmin)
    {
        var isParticipant = match.Player1Id == requestingUserId || match.Player2Id == requestingUserId;
        if (!isParticipant && !isAdmin)
            throw new UnauthorizedAccessException("Only participants or admins can view match details.");

        var isPlayer1 = match.Player1Id == requestingUserId;
        var now = DateTime.UtcNow;

        var currentUserRole = isAdmin ? "ADMIN"
            : isPlayer1 ? "PLAYER1"
            : isParticipant ? "PLAYER2"
            : "SPECTATOR";

        // Scramble — available in SCRAMBLE_CHECKING phase and beyond (before match end)
        var phase = match.Phase;
        var canSeeScramble = isParticipant && match.StatusCode is not nameof(OnlineMatchStatus.CANCELLED);

        // Player 1 checklist
        var p1ChecklistPassed = IsChecklistPassed(match, true);
        var p2ChecklistPassed = IsChecklistPassed(match, false);

        // CooldownUntil — only visible to the requesting user
        var p1CooldownUntil = isPlayer1 ? match.Player1Profile?.MatchmakingCooldownUntil : null;
        var p2CooldownUntil = !isPlayer1 ? match.Player2Profile?.MatchmakingCooldownUntil : null;

        return new OnlineMatchStateDto
        {
            MatchId = match.Id,
            PuzzleTypeId = match.PuzzleTypeId,
            Status = match.StatusCode,
            Phase = phase,
            ServerNow = now,

            SetupDeadlineAt = match.SetupDeadlineAt,
            ReadyDeadlineAt = match.ReadyDeadlineAt,
            CountdownEndsAt = match.CountdownEndsAt,
            InspectionDeadlineAt = match.InspectionDeadlineAt,
            SolveDeadlineAt = match.SolveDeadlineAt,
            FinishCheckDeadlineAt = match.FinishCheckDeadlineAt,
            VideoEvidenceUploadDeadlineAt = match.VideoEvidenceUploadDeadlineAt,

            CancelReason = match.CancelReason,
            TimeoutPlayerId = match.TimeoutPlayerId,
            EloChanged = match.EloChanged,

            CurrentUserRole = currentUserRole,
            ScrambleSequence = canSeeScramble ? match.ScrambleSequence : null,

            WinnerId = match.WinnerId,
            Outcome = match.Outcome,
            ReviewReasonJson = match.ReviewReasonJson,

            StartedAt = match.StartedAt,
            ScrambleRevealedAt = match.ScrambleRevealedAt,
            EndedAt = match.EndedAt,
            CreatedAt = match.CreatedAt,
            TimeLimitMs = match.TimeLimitMs,

            Player1 = new OnlineMatchPlayerStateDto
            {
                UserId = match.Player1Id,
                DisplayName = match.Player1?.DisplayName,
                CameraReady = match.Player1CameraReady,
                WebRtcConnected = match.Player1WebRtcConnected,
                RecordingStarted = match.Player1RecordingStarted,
                TimerReady = match.Player1TimerReady,
                ChecklistPassed = p1ChecklistPassed,
                PlayerReady = match.Player1Ready,
                ScrambleCheckStatus = match.Player1ScrambleCheckStatus,
                FinishCheckStatus = match.Player1FinishCheckStatus,
                AiPreCheckStatus = match.Player1AiPreCheckStatus,
                ResultStatus = match.Player1ResultStatus,
                TimeMs = match.Player1TimeMs,
                EloBefore = match.Player1EloBefore ?? match.Player1Profile?.EloStandard ?? 1000,
                EloAfter = match.Player1EloAfter,
                IsDnf = match.Player1IsDnf,
                FinishedAt = match.Player1FinishedAt,
                CooldownUntil = p1CooldownUntil
            },

            Player2 = new OnlineMatchPlayerStateDto
            {
                UserId = match.Player2Id,
                DisplayName = match.Player2?.DisplayName,
                CameraReady = match.Player2CameraReady,
                WebRtcConnected = match.Player2WebRtcConnected,
                RecordingStarted = match.Player2RecordingStarted,
                TimerReady = match.Player2TimerReady,
                ChecklistPassed = p2ChecklistPassed,
                PlayerReady = match.Player2Ready,
                ScrambleCheckStatus = match.Player2ScrambleCheckStatus,
                FinishCheckStatus = match.Player2FinishCheckStatus,
                AiPreCheckStatus = match.Player2AiPreCheckStatus,
                ResultStatus = match.Player2ResultStatus,
                TimeMs = match.Player2TimeMs,
                EloBefore = match.Player2EloBefore ?? match.Player2Profile?.EloStandard ?? 1000,
                EloAfter = match.Player2EloAfter,
                IsDnf = match.Player2IsDnf,
                FinishedAt = match.Player2FinishedAt,
                CooldownUntil = p2CooldownUntil
            }
        };
    }

    /// <summary>Build a lightweight state payload for SignalR events.</summary>
    public static object BuildSignalRStatePayload(OnlineMatch match, string? message = null)
    {
        var now = DateTime.UtcNow;
        return new
        {
            matchId = match.Id,
            status = match.StatusCode,
            phase = match.Phase,
            serverNow = now,
            deadlineAt = GetCurrentDeadline(match),
            setupDeadlineAt = match.SetupDeadlineAt,
            readyDeadlineAt = match.ReadyDeadlineAt,
            countdownEndsAt = match.CountdownEndsAt,
            inspectionDeadlineAt = match.InspectionDeadlineAt,
            solveDeadlineAt = match.SolveDeadlineAt,
            finishCheckDeadlineAt = match.FinishCheckDeadlineAt,
            videoEvidenceUploadDeadlineAt = match.VideoEvidenceUploadDeadlineAt,
            cancelReason = match.CancelReason,
            timeoutPlayerId = match.TimeoutPlayerId,
            eloChanged = match.EloChanged,
            player1 = new
            {
                userId = match.Player1Id,
                cameraReady = match.Player1CameraReady,
                webRtcConnected = match.Player1WebRtcConnected,
                recordingStarted = match.Player1RecordingStarted,
                timerReady = match.Player1TimerReady,
                checklistPassed = IsChecklistPassed(match, true),
                playerReady = match.Player1Ready,
                scrambleCheckStatus = match.Player1ScrambleCheckStatus,
                resultStatus = match.Player1ResultStatus,
                finishCheckStatus = match.Player1FinishCheckStatus
            },
            player2 = new
            {
                userId = match.Player2Id,
                cameraReady = match.Player2CameraReady,
                webRtcConnected = match.Player2WebRtcConnected,
                recordingStarted = match.Player2RecordingStarted,
                timerReady = match.Player2TimerReady,
                checklistPassed = IsChecklistPassed(match, false),
                playerReady = match.Player2Ready,
                scrambleCheckStatus = match.Player2ScrambleCheckStatus,
                resultStatus = match.Player2ResultStatus,
                finishCheckStatus = match.Player2FinishCheckStatus
            },
            message
        };
    }

    /// <summary>Returns the relevant deadline for the current phase.</summary>
    public static DateTime? GetCurrentDeadline(OnlineMatch match)
        => match.Phase switch
        {
            "ROOM_SETUP" or "WEBRTC_CONNECTING" or "MOBILE_TIMER_PAIRING" or "SCRAMBLE_CHECKING" => match.SetupDeadlineAt,
            "WAITING_READY" => match.ReadyDeadlineAt,
            "COUNTDOWN" => match.CountdownEndsAt,
            "INSPECTION" => match.InspectionDeadlineAt,
            "SOLVING" => match.SolveDeadlineAt,
            "FINISH_CHECKING" => match.FinishCheckDeadlineAt,
            "PENDING_EVIDENCE" => match.VideoEvidenceUploadDeadlineAt,
            _ => null
        };

    // ===================================================================
    // Outcome helpers (unchanged)
    // ===================================================================

    public static string DetermineOutcome(OnlineMatch match)
    {
        if (match.StatusCode == nameof(OnlineMatchStatus.CANCELLED))
            return OnlineMatchOutcome.CANCELLED.ToString();

        if (match.Player1ResultStatus == PlayerResultStatus.VALID.ToString()
            && match.Player2ResultStatus == PlayerResultStatus.VALID.ToString())
        {
            if (match.Player1TimeMs < match.Player2TimeMs) return OnlineMatchOutcome.PLAYER1_WIN.ToString();
            if (match.Player2TimeMs < match.Player1TimeMs) return OnlineMatchOutcome.PLAYER2_WIN.ToString();
            return OnlineMatchOutcome.DRAW.ToString();
        }

        if (match.Player1ResultStatus == PlayerResultStatus.VALID.ToString()
            && match.Player2ResultStatus == PlayerResultStatus.DNF.ToString())
            return OnlineMatchOutcome.PLAYER1_WIN.ToString();

        if (match.Player2ResultStatus == PlayerResultStatus.VALID.ToString()
            && match.Player1ResultStatus == PlayerResultStatus.DNF.ToString())
            return OnlineMatchOutcome.PLAYER2_WIN.ToString();

        return OnlineMatchOutcome.INCONCLUSIVE.ToString();
    }

    public static bool HasOpenFraudReport(IEnumerable<FraudReport> reports)
        => reports.Any(report => report.StatusCode is "OPEN" or "REVIEWING" or "PENDING");

    public static string MergeReviewReason(string? existingJson, object reason)
    {
        var reasons = new List<object>();
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(existingJson);
                if (existing != null)
                    reasons.AddRange(existing);
            }
            catch { }
        }
        reasons.Add(reason);
        return JsonSerializer.Serialize(reasons);
    }

    // ===================================================================
    // Legacy helpers (backwards compat)
    // ===================================================================

    [Obsolete("Use BuildMatchState instead. Kept for backwards compatibility.")]
    public static OnlineMatchDetailDto BuildMatchDetail(OnlineMatch match, Guid requestingUserId, bool isAdmin)
    {
        var isParticipant = match.Player1Id == requestingUserId || match.Player2Id == requestingUserId;
        if (!isParticipant && !isAdmin)
            throw new UnauthorizedAccessException("Only participants or admins can view match details.");

        return new OnlineMatchDetailDto
        {
            Id = match.Id,
            PuzzleTypeId = match.PuzzleTypeId,
            StatusCode = match.StatusCode,
            RoomToken = match.RoomToken,
            QrSessionCode = match.QrSessionCode,
            Player1Id = match.Player1Id,
            Player2Id = match.Player2Id,
            Player1Name = match.Player1?.DisplayName,
            Player2Name = match.Player2?.DisplayName,
            Player1UserCode = match.Player1?.UserCode,
            Player2UserCode = match.Player2?.UserCode,
            WinnerId = match.WinnerId,
            WinnerName = match.WinnerId == match.Player1Id
                ? match.Player1?.DisplayName
                : match.WinnerId == match.Player2Id
                    ? match.Player2?.DisplayName
                    : null,
            Player1CameraReady = match.Player1CameraReady,
            Player2CameraReady = match.Player2CameraReady,
            Player1WebRtcConnected = match.Player1WebRtcConnected,
            Player2WebRtcConnected = match.Player2WebRtcConnected,
            Player1RecordingStarted = match.Player1RecordingStarted,
            Player2RecordingStarted = match.Player2RecordingStarted,
            Player1TimerReady = match.Player1TimerReady,
            Player2TimerReady = match.Player2TimerReady,
            Player1Ready = match.Player1Ready,
            Player2Ready = match.Player2Ready,
            Player1ScrambleCheckStatus = match.Player1ScrambleCheckStatus,
            Player2ScrambleCheckStatus = match.Player2ScrambleCheckStatus,
            Player1FinishCheckStatus = match.Player1FinishCheckStatus,
            Player2FinishCheckStatus = match.Player2FinishCheckStatus,
            Outcome = match.Outcome,
            ReviewReasonJson = match.ReviewReasonJson,
            VideoEvidenceUploadDeadlineAt = match.VideoEvidenceUploadDeadlineAt,
            Player1ResultStatus = match.Player1ResultStatus,
            Player2ResultStatus = match.Player2ResultStatus,
            Player1TimeMs = match.Player1TimeMs,
            Player2TimeMs = match.Player2TimeMs,
            Player1EloBefore = match.Player1EloBefore,
            Player1EloAfter = match.Player1EloAfter,
            Player2EloBefore = match.Player2EloBefore,
            Player2EloAfter = match.Player2EloAfter,
            StartedAt = match.StartedAt,
            ScrambleRevealedAt = match.ScrambleRevealedAt,
            EndedAt = match.EndedAt,
            ScrambleSequence = isParticipant ? match.ScrambleSequence : null,
            PlayerScrambleSequence = isParticipant ? match.ScrambleSequence : null,
            TimeLimitMs = match.TimeLimitMs
        };
    }

    [Obsolete("Use BuildSignalRStatePayload instead.")]
    public static MatchReadinessResponseDto BuildReadinessResponse(OnlineMatch match, string message)
    {
        var missing = GetMissingReadiness(match);
        return new MatchReadinessResponseDto
        {
            Message = message,
            MatchId = match.Id,
            StatusCode = match.StatusCode,
            Player1CameraReady = match.Player1CameraReady,
            Player2CameraReady = match.Player2CameraReady,
            Player1WebRtcConnected = match.Player1WebRtcConnected,
            Player2WebRtcConnected = match.Player2WebRtcConnected,
            Player1RecordingStarted = match.Player1RecordingStarted,
            Player2RecordingStarted = match.Player2RecordingStarted,
            Player1TimerReady = match.Player1TimerReady,
            Player2TimerReady = match.Player2TimerReady,
            Player1Ready = match.Player1Ready,
            Player2Ready = match.Player2Ready,
            Player1ScrambleCheckStatus = match.Player1ScrambleCheckStatus,
            Player2ScrambleCheckStatus = match.Player2ScrambleCheckStatus,
            Player1FinishCheckStatus = match.Player1FinishCheckStatus,
            Player2FinishCheckStatus = match.Player2FinishCheckStatus,
            Outcome = match.Outcome,
            IsMatchReady = missing.Count == 0,
            Missing = missing
        };
    }

    private static List<string> GetMissingReadiness(OnlineMatch match)
    {
        var missing = new List<string>();
        if (!match.Player1CameraReady) missing.Add("player1CameraReady");
        if (!match.Player2CameraReady) missing.Add("player2CameraReady");
        if (!match.Player1WebRtcConnected) missing.Add("player1WebRtcConnected");
        if (!match.Player2WebRtcConnected) missing.Add("player2WebRtcConnected");
        if (!match.Player1RecordingStarted) missing.Add("player1RecordingStarted");
        if (!match.Player2RecordingStarted) missing.Add("player2RecordingStarted");
        if (!match.Player1TimerReady) missing.Add("player1TimerReady");
        if (!match.Player2TimerReady) missing.Add("player2TimerReady");
        if (!match.Player1Ready) missing.Add("player1Ready");
        if (!match.Player2Ready) missing.Add("player2Ready");
        if (match.Player1ScrambleCheckStatus != "PASSED") missing.Add("player1ScrambleValidation");
        if (match.Player2ScrambleCheckStatus != "PASSED") missing.Add("player2ScrambleValidation");
        return missing;
    }
}
