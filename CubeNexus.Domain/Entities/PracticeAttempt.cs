using CubeNexus.Domain.Enums;

namespace CubeNexus.Domain.Entities;

public class PracticeAttempt
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string ScrambleSequence { get; set; } = string.Empty;
    public PracticeAttemptState State { get; set; } = PracticeAttemptState.Scrambled;

    public DateTime? HandsOnAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }

    public int? TimeMs { get; set; }
    public Guid? PenaltyTypeId { get; set; }
    public bool IsDnf { get; set; }
    public string? AbortReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public PracticeSession Session { get; set; } = null!;
    public PenaltyType? PenaltyType { get; set; }
    public PracticeSolve? Solve { get; set; }
}
