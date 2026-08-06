namespace CubeNexus.Application.DTOs.OnlineArena;

public class EloConfigDto
{
    public Guid Id { get; set; }
    public int KFactorPlacement { get; set; }
    public int KFactorStandard { get; set; }
    public int PlacementMatchCount { get; set; }
    public int DefaultElo { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public class UpdateEloConfigRequest
{
    public int KFactorPlacement { get; set; } = 100;
    public int KFactorStandard { get; set; } = 20;
    public int PlacementMatchCount { get; set; } = 5;
    public int DefaultElo { get; set; } = 1000;
}

public class AdminPlayerEloDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string PuzzleTypeName { get; set; } = "3x3x3";
    public int EloStandard { get; set; }
    public int PeakEloStandard { get; set; }
    public int TotalWinsStandard { get; set; }
    public int TotalLossesStandard { get; set; }
    public int TotalDrawsStandard { get; set; }
    public bool IsPlacementCompleteStandard { get; set; }
    public int PlacementMatchesDoneStandard { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AdjustPlayerEloRequest
{
    public Guid PuzzleTypeId { get; set; }
    public int EloDelta { get; set; }
    public string? Reason { get; set; }
}

public class AdjustPlayerEloResponseDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public int EloBefore { get; set; }
    public int EloAfter { get; set; }
    public int Delta { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime AdjustedAt { get; set; }
}
