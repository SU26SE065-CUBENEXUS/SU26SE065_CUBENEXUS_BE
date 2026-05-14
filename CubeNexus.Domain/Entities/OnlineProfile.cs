namespace CubeNexus.Domain.Entities;

public class OnlineProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public int Elo { get; set; } = 1000;
    public int? PeakElo { get; set; }
    public int PlacementMatchesDone { get; set; } = 0;
    public bool IsPlacementComplete { get; set; } = false;
    public string? SeedSourceCode { get; set; }
    public int TotalWins { get; set; } = 0;
    public int TotalLosses { get; set; } = 0;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public PuzzleType PuzzleType { get; set; } = null!;
}
