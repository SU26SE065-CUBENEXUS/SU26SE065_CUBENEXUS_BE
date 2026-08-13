namespace CubeNexus.Domain.Entities;

public class OnlineAsyncAttempt
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public Guid UserId { get; set; }
    public string ScrambleSequence { get; set; } = string.Empty;
    public Guid? ScramblePoolItemId { get; set; }

    /// <summary>
    /// Lifecycle status: INITIALIZED | SCRAMBLE_VERIFIED | SOLVING | FINISH_PENDING | COMPLETED
    /// </summary>
    public string Status { get; set; } = "INITIALIZED";

    /// <summary>
    /// Admin review status: PENDING_REVIEW | APPROVED | REJECTED
    /// </summary>
    public string ReviewStatus { get; set; } = "PENDING_REVIEW";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? HandTimerStartedAt { get; set; }
    public DateTime? SolveStartedAt { get; set; }
    public DateTime? SolveFinishedAt { get; set; }
    /// <summary>Absolute server-side deadline, started only after scramble verification.</summary>
    public DateTime? AttemptDeadlineAt { get; set; }

    public int? RawTimeMs { get; set; }
    public int PenaltyTimeMs { get; set; } = 0;
    public string PenaltyCode { get; set; } = "NONE"; // NONE | PLUS2 | DNF
    public bool IsDnf { get; set; } = false;
    public int? FinalTimeMs { get; set; }

    public string ScrambleCheckStatus { get; set; } = "PENDING"; // PENDING | PASSED | FAILED
    public string FinishCheckStatus { get; set; } = "PENDING"; // PENDING | PASSED | FAILED

    public string? VideoEvidenceUrl { get; set; }
    public string? ScrambleEvidenceJson { get; set; }
    public string? FinishEvidenceJson { get; set; }

    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Tournament Tournament { get; set; } = null!;
    public User User { get; set; } = null!;
    public User? ReviewedByUser { get; set; }
    public ScramblePoolItem? ScramblePoolItem { get; set; }
}
