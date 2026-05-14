namespace CubeNexus.Domain.Entities;

public class PracticeSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public User User { get; set; } = null!;
    public PuzzleType PuzzleType { get; set; } = null!;
}
