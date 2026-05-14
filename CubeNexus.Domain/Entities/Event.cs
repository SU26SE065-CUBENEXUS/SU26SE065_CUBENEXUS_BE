namespace CubeNexus.Domain.Entities;

public class Event
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string EventFormatCode { get; set; } = string.Empty;
    public int? TimeLimitMs { get; set; }
    public int? CutoffTimeMs { get; set; }
    public int SolveCount { get; set; } = 5;
    public int? SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
    public PuzzleType PuzzleType { get; set; } = null!;
}
