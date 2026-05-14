namespace CubeNexus.Domain.Entities;

public class Scramble
{
    public Guid Id { get; set; }
    public Guid ScrambleSetId { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public int SolveNumber { get; set; }
    public string Sequence { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ScrambleSet ScrambleSet { get; set; } = null!;
    public PuzzleType PuzzleType { get; set; } = null!;
}
