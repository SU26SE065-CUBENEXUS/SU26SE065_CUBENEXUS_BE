namespace CubeNexus.Application.DTOs.Arena;

/// <summary>Hồ sơ Online Arena đầy đủ của người chơi.</summary>
public class OnlineProfileDto
{
    public Guid UserId { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string PuzzleTypeName { get; set; } = string.Empty;

    // === ELO ===
    /// <summary>
    /// Elo hiển thị. NULL nếu chưa hoàn thành Placement Phase
    /// (Elo ẩn trong 5 trận đầu).
    /// </summary>
    public int? EloVisible { get; set; }

    /// <summary>Elo đỉnh cao từng đạt.</summary>
    public int? PeakElo { get; set; }

    // === SEEDING ===
    public int? SeedElo { get; set; }
    public string? SeedSourceCode { get; set; }
    public int? PracticeAo5Ms { get; set; }

    // === PLACEMENT ===
    /// <summary>Số trận placement đã hoàn thành / tổng cần.</summary>
    public int PlacementMatchesDone { get; set; }
    public int PlacementMatchCount { get; set; }
    public bool IsPlacementComplete { get; set; }
    public DateTime? PlacementCompletedAt { get; set; }

    // === THỐNG KÊ ===
    public int TotalWins { get; set; }
    public int TotalLosses { get; set; }
    public int TotalDraws { get; set; }
    public double WinRate { get; set; }

    public DateTime CreatedAt { get; set; }
}
