namespace CubeNexus.Domain.Entities;

public class OnlineMatch
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }

    /// <summary>
    /// Shared scramble sequence for both players (ranked 1v1 uses same scramble).
    /// </summary>
    public string ScrambleSequence { get; set; } = string.Empty;
    public Guid? ScramblePoolItemId { get; set; }

    public Guid Player1Id { get; set; }
    public Guid Player2Id { get; set; }
    public Guid Player1ProfileId { get; set; }
    public Guid Player2ProfileId { get; set; }
    public Guid? WinnerId { get; set; }
    public string StatusCode { get; set; } = string.Empty;

    /// <summary>
    /// Fine-grained phase within the match lifecycle.
    /// Valid values: ROOM_SETUP | WEBRTC_CONNECTING | MOBILE_TIMER_PAIRING | SCRAMBLE_CHECKING |
    ///               WAITING_READY | COUNTDOWN | INSPECTION | SOLVING | FINISH_CHECKING |
    ///               PENDING_EVIDENCE | COMPLETED | NEEDS_REVIEW | CANCELLED
    /// </summary>
    public string Phase { get; set; } = "ROOM_SETUP";

    public string RoomToken { get; set; } = string.Empty;
    public string? QrSessionCode { get; set; }

    public int? Player1TimeMs { get; set; }
    public int? Player2TimeMs { get; set; }
    public int? Player1EloBefore { get; set; }
    public int? Player2EloBefore { get; set; }
    public int? Player1EloAfter { get; set; }
    public int? Player2EloAfter { get; set; }

    // === Deadlines (UTC) ===
    /// <summary>Setup deadline: match created + 5 minutes.</summary>
    public DateTime? SetupDeadlineAt { get; set; }

    /// <summary>Ready deadline: both checklist passed + 90 seconds.</summary>
    public DateTime? ReadyDeadlineAt { get; set; }

    /// <summary>Countdown ends: both playerReady + 5 seconds.</summary>
    public DateTime? CountdownEndsAt { get; set; }

    /// <summary>Inspection deadline: countdown ends + 15 seconds.</summary>
    public DateTime? InspectionDeadlineAt { get; set; }

    /// <summary>Solve deadline: inspection ends + 10 minutes.</summary>
    public DateTime? SolveDeadlineAt { get; set; }

    /// <summary>Finish check deadline: last timer stopped + 2 minutes.</summary>
    public DateTime? FinishCheckDeadlineAt { get; set; }

    // === Cancellation info ===
    /// <summary>Why the match was cancelled (SETUP_TIMEOUT | READY_TIMEOUT | PLAYER_LEFT | etc.).</summary>
    public string? CancelReason { get; set; }

    /// <summary>The player who caused the timeout/cancellation.</summary>
    public Guid? TimeoutPlayerId { get; set; }

    /// <summary>Whether Elo was changed for this match.</summary>
    public bool EloChanged { get; set; } = false;

    /// <summary>Idempotency guard: set when setup timeout penalty has been applied, prevents BackgroundService double-apply.</summary>
    public DateTime? SetupTimeoutPenaltyAppliedAt { get; set; }

    // === Checklist / Readiness Fields ===
    // checklistPassed = camera + webRtc + recording + timer + scrambleCheck PASSED (all true)
    // playerReady = player explicitly clicked "Ready" after checklistPassed
    public bool Player1CameraReady { get; set; } = true;
    public bool Player2CameraReady { get; set; } = true;
    public bool Player1WebRtcConnected { get; set; } = false;
    public bool Player2WebRtcConnected { get; set; } = false;
    public bool Player1RecordingStarted { get; set; } = false;
    public bool Player2RecordingStarted { get; set; } = false;
    public bool Player1TimerReady { get; set; } = false;
    public bool Player2TimerReady { get; set; } = false;
    public bool Player1Ready { get; set; } = false;
    public bool Player2Ready { get; set; } = false;
    public string Player1AiPreCheckStatus { get; set; } = "PENDING";
    public string Player2AiPreCheckStatus { get; set; } = "PENDING";
    public string Player1ScrambleCheckStatus { get; set; } = "PENDING";
    public string Player2ScrambleCheckStatus { get; set; } = "PENDING";
    public string Player1FinishCheckStatus { get; set; } = "PENDING";
    public string Player2FinishCheckStatus { get; set; } = "PENDING";

    // === Scramble state (shared — both players use same sequence) ===
    public string? Player1ExpectedStateJson { get; set; }
    public string? Player2ExpectedStateJson { get; set; }
    public string? Player1ObservedStateJson { get; set; }
    public string? Player2ObservedStateJson { get; set; }
    public string? Player1ScannerStateJson { get; set; }
    public string? Player2ScannerStateJson { get; set; }
    public string Outcome { get; set; } = "INCONCLUSIVE";
    public string? ReviewReasonJson { get; set; }
    public DateTime? VideoEvidenceUploadDeadlineAt { get; set; }
    public DateTime? Player1RecordingStartedAt { get; set; }
    public DateTime? Player2RecordingStartedAt { get; set; }
    public int TimeLimitMs { get; set; } = 480000;

    // === Result Fields ===
    public bool Player1IsDnf { get; set; } = false;
    public bool Player2IsDnf { get; set; } = false;
    public string Player1ResultStatus { get; set; } = "PENDING";
    public string Player2ResultStatus { get; set; } = "PENDING";

    // === Realtime Timestamps ===
    public DateTime? ScrambleRevealedAt { get; set; }
    public DateTime? Player1FinishedAt { get; set; }
    public DateTime? Player2FinishedAt { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // === Navigation ===
    public PuzzleType PuzzleType { get; set; } = null!;
    public ScramblePoolItem? ScramblePoolItem { get; set; }
    public User Player1 { get; set; } = null!;
    public User Player2 { get; set; } = null!;
    public OnlineProfile Player1Profile { get; set; } = null!;
    public OnlineProfile Player2Profile { get; set; } = null!;
    public User? Winner { get; set; }
    public User? TimeoutPlayer { get; set; }
    public ICollection<OnlineMatchAiCheck> AiChecks { get; set; } = new List<OnlineMatchAiCheck>();
    public ICollection<OnlineMatchVideoEvidence> VideoEvidences { get; set; } = new List<OnlineMatchVideoEvidence>();
    public ICollection<OnlineMatchAuditLog> AuditLogs { get; set; } = new List<OnlineMatchAuditLog>();
}
