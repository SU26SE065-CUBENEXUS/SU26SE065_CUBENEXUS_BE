namespace CubeNexus.Application.DTOs.Arena;

/// <summary>Kết quả sau khi ghi nhận một trận đấu Online.</summary>
public class MatchResultDto
{
    public Guid MatchId { get; set; }

    public PlayerEloChangeDto Player1 { get; set; } = null!;
    public PlayerEloChangeDto Player2 { get; set; } = null!;

    /// <summary>Trận này có phải là trận placement không.</summary>
    public bool IsPlacementMatch { get; set; }

    /// <summary>Player1 vừa hoàn thành Placement Phase sau trận này.</summary>
    public bool Player1PlacementCompleted { get; set; }

    /// <summary>Player2 vừa hoàn thành Placement Phase sau trận này.</summary>
    public bool Player2PlacementCompleted { get; set; }
}

/// <summary>Thay đổi Elo của một người chơi sau trận.</summary>
public class PlayerEloChangeDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public int EloBefore { get; set; }
    public int EloAfter { get; set; }
    public int Delta { get; set; }

    public decimal ActualScore { get; set; }
    public decimal ExpectedScore { get; set; }
    public int KFactorUsed { get; set; }

    /// <summary>Trận placement thứ mấy (nếu chưa complete).</summary>
    public int PlacementMatchesDone { get; set; }
    public bool IsPlacementComplete { get; set; }
}
