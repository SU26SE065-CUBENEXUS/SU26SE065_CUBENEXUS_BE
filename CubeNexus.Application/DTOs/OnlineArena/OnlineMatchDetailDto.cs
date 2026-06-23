namespace CubeNexus.Application.DTOs.OnlineArena;

public class OnlineMatchDetailDto
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string RoomToken { get; set; } = string.Empty;
    public string? QrSessionCode { get; set; }
    public Guid Player1Id { get; set; }
    public Guid Player2Id { get; set; }
    public Guid? WinnerId { get; set; }
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
    public string Outcome { get; set; } = string.Empty;
    public string? ReviewReasonJson { get; set; }
    public DateTime? VideoEvidenceUploadDeadlineAt { get; set; }
    public string Player1ResultStatus { get; set; } = string.Empty;
    public string Player2ResultStatus { get; set; } = string.Empty;
    public int? Player1TimeMs { get; set; }
    public int? Player2TimeMs { get; set; }
    public int? Player1EloBefore { get; set; }
    public int? Player1EloAfter { get; set; }
    public int? Player2EloBefore { get; set; }
    public int? Player2EloAfter { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? ScrambleRevealedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? ScrambleSequence { get; set; }
    public string? PlayerScrambleSequence { get; set; }
    public int TimeLimitMs { get; set; }
}
