namespace CubeNexus.Domain.Entities;

public class OnlineMatch
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string ScrambleSequence { get; set; } = string.Empty;
    public Guid Player1Id { get; set; }
    public Guid Player2Id { get; set; }
    public Guid Player1ProfileId { get; set; }
    public Guid Player2ProfileId { get; set; }
    public Guid? WinnerId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string RoomToken { get; set; } = string.Empty;
    public string? QrSessionCode { get; set; }
    
    public int? Player1TimeMs { get; set; }
    public int? Player2TimeMs { get; set; }
    public int? Player1EloBefore { get; set; }
    public int? Player2EloBefore { get; set; }
    public int? Player1EloAfter { get; set; }
    public int? Player2EloAfter { get; set; }

    // Readiness Fields
    public bool Player1CameraReady { get; set; } = false;
    public bool Player2CameraReady { get; set; } = false;
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
    public string? Player1ScrambleSequence { get; set; }
    public string? Player2ScrambleSequence { get; set; }
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

    // Result Fields
    public bool Player1IsDnf { get; set; } = false;
    public bool Player2IsDnf { get; set; } = false;
    public string Player1ResultStatus { get; set; } = "PENDING";
    public string Player2ResultStatus { get; set; } = "PENDING";

    // Realtime Timestamps
    public DateTime? ScrambleRevealedAt { get; set; }
    public DateTime? Player1FinishedAt { get; set; }
    public DateTime? Player2FinishedAt { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public PuzzleType PuzzleType { get; set; } = null!;
    public User Player1 { get; set; } = null!;
    public User Player2 { get; set; } = null!;
    public OnlineProfile Player1Profile { get; set; } = null!;
    public OnlineProfile Player2Profile { get; set; } = null!;
    public User? Winner { get; set; }
    public ICollection<OnlineMatchAiCheck> AiChecks { get; set; } = new List<OnlineMatchAiCheck>();
    public ICollection<OnlineMatchVideoEvidence> VideoEvidences { get; set; } = new List<OnlineMatchVideoEvidence>();
    public ICollection<OnlineMatchAuditLog> AuditLogs { get; set; } = new List<OnlineMatchAuditLog>();
}
