namespace CubeNexus.Application.DTOs.OnlineArena;

/// <summary>
/// DTO chuẩn dùng cho mọi response liên quan đến trạng thái match.
/// Frontend map trực tiếp từ status + phase, không cần tự suy luận từ boolean rời rạc.
/// </summary>
public class OnlineMatchStateDto
{
    public Guid MatchId { get; set; }
    public Guid PuzzleTypeId { get; set; }

    /// <summary>Match status: CREATED | READY | ONGOING | PENDING_EVIDENCE | COMPLETED | NEEDS_REVIEW | CANCELLED | DRAW</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Fine-grained phase:
    /// ROOM_SETUP | WEBRTC_CONNECTING | MOBILE_TIMER_PAIRING | SCRAMBLE_CHECKING |
    /// WAITING_READY | COUNTDOWN | INSPECTION | SOLVING | FINISH_CHECKING |
    /// PENDING_EVIDENCE | COMPLETED | NEEDS_REVIEW | CANCELLED
    /// </summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>Current UTC time from server — frontend phải dùng cái này để tính countdown, không dùng local time.</summary>
    public DateTime ServerNow { get; set; }

    // === Deadlines (UTC) ===
    public DateTime? SetupDeadlineAt { get; set; }
    public DateTime? ReadyDeadlineAt { get; set; }
    public DateTime? CountdownEndsAt { get; set; }
    public DateTime? InspectionDeadlineAt { get; set; }
    public DateTime? SolveDeadlineAt { get; set; }
    public DateTime? FinishCheckDeadlineAt { get; set; }
    public DateTime? VideoEvidenceUploadDeadlineAt { get; set; }

    // === Cancellation info ===
    public string? CancelReason { get; set; }
    public Guid? TimeoutPlayerId { get; set; }
    public bool EloChanged { get; set; }

    // === Who is the current requester relative to this match ===
    /// <summary>PLAYER1 | PLAYER2 | SPECTATOR | ADMIN</summary>
    public string CurrentUserRole { get; set; } = "SPECTATOR";

    // === Shared scramble ===
    /// <summary>Scramble sequence — available from SCRAMBLE_CHECKING phase (before ONGOING).</summary>
    public string? ScrambleSequence { get; set; }

    // === Player states ===
    public OnlineMatchPlayerStateDto Player1 { get; set; } = new();
    public OnlineMatchPlayerStateDto Player2 { get; set; } = new();

    // === Result data (available after completion) ===
    public Guid? WinnerId { get; set; }
    public string Outcome { get; set; } = "INCONCLUSIVE";
    public string? ReviewReasonJson { get; set; }

    // === Timestamps ===
    public DateTime? StartedAt { get; set; }
    public DateTime? ScrambleRevealedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public int TimeLimitMs { get; set; }
}

/// <summary>
/// State của một player trong match — tách rõ checklistPassed vs playerReady.
/// </summary>
public class OnlineMatchPlayerStateDto
{
    public Guid UserId { get; set; }
    public string? DisplayName { get; set; }

    // === Checklist items ===
    public bool CameraReady { get; set; }
    public bool WebRtcConnected { get; set; }
    public bool RecordingStarted { get; set; }
    public bool TimerReady { get; set; }

    /// <summary>
    /// Computed: cameraReady AND webRtcConnected AND recordingStarted AND timerReady AND scrambleCheckStatus == PASSED.
    /// Đây là điều kiện để phase chuyển WAITING_READY.
    /// </summary>
    public bool ChecklistPassed { get; set; }

    /// <summary>
    /// Player đã bấm nút Ready sau khi ChecklistPassed.
    /// Đây là điều kiện để phase chuyển COUNTDOWN.
    /// KHÔNG nhầm với ChecklistPassed.
    /// </summary>
    public bool PlayerReady { get; set; }

    // === Validation statuses ===
    public string ScrambleCheckStatus { get; set; } = "PENDING";
    public string FinishCheckStatus { get; set; } = "PENDING";
    public string AiPreCheckStatus { get; set; } = "PENDING";

    // === Result ===
    public string ResultStatus { get; set; } = "PENDING";
    public int? TimeMs { get; set; }
    public int? EloBefore { get; set; }
    public int? EloAfter { get; set; }
    public bool IsDnf { get; set; }
    public DateTime? FinishedAt { get; set; }

    // === Matchmaking cooldown (visible to the player themselves) ===
    public DateTime? CooldownUntil { get; set; }
}

/// <summary>
/// Opponent summary shown during MATCH_FOUND phase.
/// </summary>
public class OpponentDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Rating { get; set; }
}

/// <summary>
/// Kết quả matchmaking — thêm cooldown info và confirmation info.
/// </summary>
public class MatchmakingStatusDto
{
    /// <summary>IDLE | QUEUED | MATCH_FOUND | MATCHED | IN_ACTIVE_MATCH | COOLDOWN</summary>
    public string Status { get; set; } = string.Empty;
    public Guid? QueueId { get; set; }
    public Guid? MatchId { get; set; }
    public string? MatchStatus { get; set; }
    public string? RoomToken { get; set; }
    public string? QrSessionCode { get; set; }
    public Guid? MeUserId { get; set; }
    public Guid? OpponentUserId { get; set; }

    // Cooldown info (khi status = COOLDOWN)
    public DateTime? CooldownUntil { get; set; }
    public int? RemainingSeconds { get; set; }
    public DateTime ServerNow { get; set; } = DateTime.UtcNow;

    // === Match Confirmation fields (khi status = MATCH_FOUND) ===
    /// <summary>Id of the OnlineMatchConfirmation — dùng để gọi /confirm.</summary>
    public Guid? ConfirmationId { get; set; }
    /// <summary>Opponent summary shown during MATCH_FOUND phase.</summary>
    public OpponentDto? Opponent { get; set; }
    /// <summary>UTC deadline — player phải confirm trước thời điểm này.</summary>
    public DateTime? ConfirmDeadlineAt { get; set; }
    /// <summary>Whether player1 has confirmed.</summary>
    public bool? Player1Confirmed { get; set; }
    /// <summary>Whether player2 has confirmed.</summary>
    public bool? Player2Confirmed { get; set; }
    /// <summary>Whether the requesting user occupies player slot 1.</summary>
    public bool? IsPlayer1 { get; set; }

    // Setup deadline (khi status = MATCHED)
    public DateTime? SetupDeadlineAt { get; set; }
}
