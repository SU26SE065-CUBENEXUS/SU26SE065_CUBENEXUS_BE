namespace CubeNexus.Application.DTOs.OnlineArena;

public class OnlineProfileDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Elo { get; set; }
    public int? PeakElo { get; set; }
    public int PlacementMatchesDone { get; set; }
    public int PlacementMatchCount { get; set; } = 5;
    public bool IsPlacementComplete { get; set; }
    public int TotalWins { get; set; }
    public int TotalLosses { get; set; }
    public int TotalDraws { get; set; }
}
