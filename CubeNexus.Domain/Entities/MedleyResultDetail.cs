namespace CubeNexus.Domain.Entities;

public class MedleyResultDetail
{
    public Guid Id { get; set; }
    public Guid ResultId { get; set; }
    public Guid MedleyPuzzleId { get; set; }
    public int? RawTimeMs { get; set; }
    public Guid? PenaltyTypeId { get; set; }
    public bool IsDnf { get; set; } = false;
    public int SortOrder { get; set; }

    public Result Result { get; set; } = null!;
    public MedleyEventPuzzle MedleyPuzzle { get; set; } = null!;
    public PenaltyType? PenaltyType { get; set; }
}
