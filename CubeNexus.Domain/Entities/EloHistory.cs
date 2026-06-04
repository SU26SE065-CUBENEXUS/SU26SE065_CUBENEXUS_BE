namespace CubeNexus.Domain.Entities;

public class EloHistory
{
    public Guid Id { get; set; }
    public Guid OnlineProfileId { get; set; }

    /// <summary>Trận đấu gây ra thay đổi. NULL nếu là seeding ban đầu hoặc admin điều chỉnh.</summary>
    public Guid? MatchId { get; set; }

    public int EloBefore { get; set; }
    public int EloAfter { get; set; }
    public int Delta { get; set; }

    /// <summary>Hệ số K đã dùng để tính toán lần thay đổi này.</summary>
    public int? KFactorUsed { get; set; }

    /// <summary>Kết quả thực tế S: 1.0 (thắng), 0.0 (thua), 0.5 (hòa).</summary>
    public decimal? ActualScore { get; set; }

    /// <summary>Kết quả kỳ vọng E theo công thức Elo: E = 1 / (1 + 10^((Rb-Ra)/400)).</summary>
    public decimal? ExpectedScore { get; set; }

    /// <summary>TRUE nếu đây là trận trong giai đoạn Placement Phase.</summary>
    public bool IsPlacementMatch { get; set; } = false;

    /// <summary>
    /// Mã lý do: 'SEEDING_INIT', 'PLACEMENT_MATCH', 'STANDARD_MATCH', 'ADMIN_ADJUST'.
    /// </summary>
    public string? ReasonCode { get; set; }

    public DateTime ChangedAt { get; set; }

    // Navigation properties
    public OnlineProfile OnlineProfile { get; set; } = null!;
    public OnlineMatch? Match { get; set; }
}
