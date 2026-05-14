namespace CubeNexus.Domain.Entities;

public class MedleyEventPuzzle
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public int SortOrder { get; set; }

    public Event Event { get; set; } = null!;
    public PuzzleType PuzzleType { get; set; } = null!;
}
