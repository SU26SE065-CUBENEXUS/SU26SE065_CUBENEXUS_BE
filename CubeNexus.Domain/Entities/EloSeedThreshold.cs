namespace CubeNexus.Domain.Entities;

public class EloSeedThreshold
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public int? MaxTimeMs { get; set; }
    public int? MinTimeMs { get; set; }
    public int EloValue { get; set; }
    public int SortOrder { get; set; }

    public PuzzleType PuzzleType { get; set; } = null!;
}
