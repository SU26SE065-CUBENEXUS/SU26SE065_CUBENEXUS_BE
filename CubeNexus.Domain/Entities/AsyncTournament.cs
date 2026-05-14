namespace CubeNexus.Domain.Entities;

public class AsyncTournament
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ScrambleSequence { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public PuzzleType PuzzleType { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
