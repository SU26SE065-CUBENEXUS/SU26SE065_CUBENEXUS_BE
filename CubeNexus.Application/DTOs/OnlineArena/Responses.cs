namespace CubeNexus.Application.DTOs.OnlineArena;

public class MatchReadinessResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public bool Player1CameraReady { get; set; }
    public bool Player2CameraReady { get; set; }
    public bool Player1WebRtcConnected { get; set; }
    public bool Player2WebRtcConnected { get; set; }
    public bool Player1RecordingStarted { get; set; }
    public bool Player2RecordingStarted { get; set; }
    public bool Player1TimerReady { get; set; }
    public bool Player2TimerReady { get; set; }
    public bool Player1Ready { get; set; }
    public bool Player2Ready { get; set; }
    public string Player1ScrambleCheckStatus { get; set; } = string.Empty;
    public string Player2ScrambleCheckStatus { get; set; } = string.Empty;
    public string Player1FinishCheckStatus { get; set; } = string.Empty;
    public string Player2FinishCheckStatus { get; set; } = string.Empty;
    public string? Outcome { get; set; }
    public bool IsMatchReady { get; set; }
    public List<string> Missing { get; set; } = [];
}

public class MobileTimerConnectResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public Guid PlayerId { get; set; }
    public Guid SessionId { get; set; }
    public bool Player1TimerReady { get; set; }
    public bool Player2TimerReady { get; set; }
    public string? DeviceInfo { get; set; }
}

public class MobileTimerDisconnectResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public bool IsActive { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}

public class StartMatchResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string ScrambleSequence { get; set; } = string.Empty;
    public string PlayerScrambleSequence { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
    public DateTime? ScrambleRevealedAt { get; set; }
    public int TimeLimitMs { get; set; }
}

public class SubmitResultResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
    public string? Outcome { get; set; }
    public string? PlayerResultStatus { get; set; }
    public string? Player1ResultStatus { get; set; }
    public string? Player2ResultStatus { get; set; }
    public int? Player1TimeMs { get; set; }
    public int? Player2TimeMs { get; set; }
    public int? Player1EloBefore { get; set; }
    public int? Player1EloAfter { get; set; }
    public int? Player2EloBefore { get; set; }
    public int? Player2EloAfter { get; set; }
    public Guid? WinnerId { get; set; }
    public bool IsMatchCompleted { get; set; }
}

public class CancelMatchResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
}

public class FraudReportDto
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid ReporterUserId { get; set; }
    public Guid ReportedUserId { get; set; }
    public string? ReasonCode { get; set; }
    public string? Description { get; set; }
    public string? EvidenceUrl { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string ReviewScope { get; set; } = string.Empty;
    public string? Decision { get; set; }
    public string? PenaltyAction { get; set; }
    public Guid? ResolvedByAdminId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public string? VerdictCode { get; set; }
    public string? AdminNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public class OnlineMatchAiCheckDto
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public string CheckType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public Guid? VideoEvidenceId { get; set; }
    public string? ModelVersion { get; set; }
    public string? ResultJson { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OnlineMatchVideoEvidenceDto
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public string? ObjectKey { get; set; }
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public double? DurationSeconds { get; set; }
    public string RecordingStatus { get; set; } = string.Empty;
    public DateTime? RecordedAt { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public long? DurationMs { get; set; }
    public DateTime? RecordingStartedAt { get; set; }
    public DateTime? RecordingEndedAt { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Checksum { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? MimeType { get; set; }
}

public class OnlineMatchAuditLogDto
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid? PlayerId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FraudReportDetailDto
{
    public FraudReportDto Report { get; set; } = new();
    public OnlineMatchDetailDto Match { get; set; } = new();
    public List<OnlineMatchAiCheckDto> AiChecks { get; set; } = [];
    public List<OnlineMatchVideoEvidenceDto> VideoEvidences { get; set; } = [];
    public List<OnlineMatchAuditLogDto> AuditLogs { get; set; } = [];
}

public class AiRubikCheckResponseDto
{
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public string CheckType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool DetectedCube { get; set; }
    public int DetectedStickers { get; set; }
    public List<List<string>>? Grid3x3 { get; set; }
    public string? Reason { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public bool ModelLoaded { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public string? ExpectedScramble { get; set; }
    public string? DetectedState { get; set; }
    public bool? IsScrambleMatched { get; set; }
    public bool? IsSolved { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class VideoEvidenceUploadResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid VideoEvidenceId { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string MatchStatus { get; set; } = string.Empty;
    public DateTime? VideoEvidenceUploadDeadlineAt { get; set; }
}

public class WebRtcConnectionResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public bool Player1WebRtcConnected { get; set; }
    public bool Player2WebRtcConnected { get; set; }
    public string StatusCode { get; set; } = string.Empty;
}

public class VideoRecordingStartedResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public bool Player1RecordingStarted { get; set; }
    public bool Player2RecordingStarted { get; set; }
    public DateTime RecordingStartedAt { get; set; }
    public string StatusCode { get; set; } = string.Empty;
}

public class SubmitSolveTimeResponseDto
{
    public Guid MatchId { get; set; }
    public Guid MeUserId { get; set; }
    public string MyResultStatus { get; set; } = string.Empty;
    public int? MyTimeMs { get; set; }
    public string MyFinishCheckStatus { get; set; } = string.Empty;
    public string OpponentResultStatus { get; set; } = string.Empty;
    public string OpponentFinishCheckStatus { get; set; } = string.Empty;
    public bool CanStartFinishCheck { get; set; }
    public string MatchPhase { get; set; } = string.Empty;
    public DateTime ServerNow { get; set; }
}

public class OnlineArenaScannerStartResponseDto
{
    public Guid ScanSessionId { get; set; }
    public string? AiSessionId { get; set; }
    public int ScanGeneration { get; set; }
    public int RequestedFaceIndex { get; set; }
    public string ScanStatus { get; set; } = string.Empty;
    public string FinishCheckStatus { get; set; } = string.Empty;
    public DateTime ServerNow { get; set; }
}

public class ObserveFinishFrameResponseDto
{
    public Guid MatchId { get; set; }
    public Guid MeUserId { get; set; }
    public string FinishCheckStatus { get; set; } = string.Empty;
    public bool WaitingForOpponent { get; set; }
    public string OpponentResultStatus { get; set; } = string.Empty;
    public string OpponentFinishCheckStatus { get; set; } = string.Empty;
    public string NextUiState { get; set; } = string.Empty;
    public DateTime ServerNow { get; set; }

    // If completed
    public string? MatchStatus { get; set; }
    public string? Outcome { get; set; }
    public Guid? WinnerId { get; set; }
}

public class RecoveryPlayerStateDto
{
    public Guid UserId { get; set; }
    public string? DisplayName { get; set; }
    public string ResultStatus { get; set; } = string.Empty;
    public int? TimeMs { get; set; }
    public string FinishCheckStatus { get; set; } = string.Empty;
    public bool IsReady { get; set; }
    /// <summary>All ROOM_SETUP checklist items complete (camera + webrtc + timer + scramble PASSED). Auto-set by backend.</summary>
    public bool ChecklistPassed { get; set; }
    public string ScrambleCheckStatus { get; set; } = string.Empty;
    public bool CameraReady { get; set; }
    public bool WebRtcConnected { get; set; }
    public bool RecordingStarted { get; set; }
    public bool TimerReady { get; set; }
}

public class RecoveryMeStateDto
{
    public Guid UserId { get; set; }
    public bool CanSubmitTime { get; set; }
    public bool CanStartFinishCheck { get; set; }
    public bool CanWatchOpponent { get; set; }
    public string NextUiState { get; set; } = string.Empty;
}

public class OnlineMatchRecoveryStateDto
{
    public Guid MatchId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string? QrSessionCode { get; set; }
    public DateTime? SetupDeadlineAt { get; set; }
    public DateTime? CountdownEndsAt { get; set; }
    public string? ScrambleSequence { get; set; }
    public DateTime? InspectionDeadlineAt { get; set; }
    public DateTime? SolveDeadlineAt { get; set; }
    public string? Outcome { get; set; }
    public Guid? WinnerId { get; set; }
    public int? Player1EloBefore { get; set; }
    public int? Player2EloBefore { get; set; }
    public int? Player1EloAfter { get; set; }
    public int? Player2EloAfter { get; set; }
    public DateTime ServerNow { get; set; }
    public RecoveryPlayerStateDto Player1 { get; set; } = null!;
    public RecoveryPlayerStateDto Player2 { get; set; } = null!;
    public RecoveryMeStateDto Me { get; set; } = null!;
}

public class OnlineMatchHistoryItemDto
{
    public Guid MatchId { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string PuzzleTypeName { get; set; } = "3x3x3";
    public string ScrambleSequence { get; set; } = string.Empty;
    public string ModeName { get; set; } = "Ranked 1v1";

    public Guid MeUserId { get; set; }
    public string MeUsername { get; set; } = string.Empty;
    public string? MeAvatarUrl { get; set; }
    public int? MeTimeMs { get; set; }
    public bool MeIsDnf { get; set; }
    public int? MeEloBefore { get; set; }
    public int? MeEloAfter { get; set; }
    public int EloChange { get; set; }

    public Guid OpponentUserId { get; set; }
    public string OpponentUsername { get; set; } = string.Empty;
    public string? OpponentAvatarUrl { get; set; }
    public int? OpponentTimeMs { get; set; }
    public bool OpponentIsDnf { get; set; }
    public int? OpponentEloBefore { get; set; }
    public int? OpponentEloAfter { get; set; }

    public bool IsWinner { get; set; }
    public bool IsDraw { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool HasVideoReplay { get; set; }
}

public class OnlineMatchHistoryResponseDto
{
    public List<OnlineMatchHistoryItemDto> Matches { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

