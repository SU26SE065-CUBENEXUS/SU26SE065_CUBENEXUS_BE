using System.Text.Json;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;

namespace CubeNexus.Application.UseCases.OnlineArena;

internal static class OnlineArenaFlowHelpers
{
    public static bool IsTerminal(string statusCode)
        => statusCode is nameof(OnlineMatchStatus.COMPLETED)
            or nameof(OnlineMatchStatus.CANCELLED)
            or nameof(OnlineMatchStatus.DRAW);

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

    public static List<string> GetMissingReadiness(OnlineMatch match)
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
            catch
            {
            }
        }

        reasons.Add(reason);
        return JsonSerializer.Serialize(reasons);
    }

    public static OnlineMatchDetailDto BuildMatchDetail(OnlineMatch match, Guid requestingUserId, bool isAdmin)
    {
        var isParticipant = match.Player1Id == requestingUserId || match.Player2Id == requestingUserId;
        if (!isParticipant && !isAdmin)
            throw new UnauthorizedAccessException("Only participants or admins can view match details.");

        var canRevealScramble = isParticipant
            && match.StatusCode is nameof(OnlineMatchStatus.ONGOING)
                or nameof(OnlineMatchStatus.PENDING_EVIDENCE)
                or nameof(OnlineMatchStatus.NEEDS_REVIEW)
                or nameof(OnlineMatchStatus.COMPLETED)
                or nameof(OnlineMatchStatus.DRAW);
        var canRevealPlayerScramble = isParticipant
            && match.StatusCode is nameof(OnlineMatchStatus.CREATED)
                or nameof(OnlineMatchStatus.READY)
                or nameof(OnlineMatchStatus.ONGOING)
                or nameof(OnlineMatchStatus.PENDING_EVIDENCE)
                or nameof(OnlineMatchStatus.NEEDS_REVIEW)
                or nameof(OnlineMatchStatus.COMPLETED)
                or nameof(OnlineMatchStatus.DRAW);

        return new OnlineMatchDetailDto
        {
            Id = match.Id,
            PuzzleTypeId = match.PuzzleTypeId,
            StatusCode = match.StatusCode,
            RoomToken = match.RoomToken,
            QrSessionCode = match.QrSessionCode,
            Player1Id = match.Player1Id,
            Player2Id = match.Player2Id,
            WinnerId = match.WinnerId,
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
            ScrambleSequence = canRevealScramble ? match.ScrambleSequence : null,
            PlayerScrambleSequence = canRevealPlayerScramble
                ? (match.Player1Id == requestingUserId ? match.Player1ScrambleSequence : match.Player2ScrambleSequence)
                : null,
            TimeLimitMs = match.TimeLimitMs
        };
    }
}
