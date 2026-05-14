namespace CubeNexus.Domain.Entities;

public class PracticeSolve
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string ScrambleSequence { get; set; } = string.Empty;
    public int TimeMs { get; set; }
    public Guid? PenaltyTypeId { get; set; }
    public bool IsDnf { get; set; } = false;
    public DateTime SolvedAt { get; set; }

    public PracticeSession Session { get; set; } = null!;
    public PenaltyType? PenaltyType { get; set; }
}
