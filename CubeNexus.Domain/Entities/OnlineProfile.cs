namespace CubeNexus.Domain.Entities;

public class OnlineProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PuzzleTypeId { get; set; }

    // === ELO HIỆN TẠI ===
    /// <summary>Điểm Elo hiện tại.</summary>
    public int Elo { get; set; } = 1000;

    /// <summary>Điểm Elo cao nhất từng đạt được.</summary>
    public int PeakElo { get; set; } = 1000;

    // === SEEDING (Giai đoạn 1) ===
    /// <summary>
    /// Elo seeding ban đầu được gán từ Practice Ao5.
    /// Lưu lại để audit và so sánh sự tiến bộ.
    /// </summary>
    public int? SeedElo { get; set; }

    /// <summary>Nguồn seeding: 'PRACTICE' hoặc 'DEFAULT'.</summary>
    public string? SeedSourceCode { get; set; }

    /// <summary>Giá trị Ao5 Practice dùng để seeding (ms).</summary>
    public int? PracticeAo5Ms { get; set; }

    /// <summary>FK tới PracticeAo5Snapshot đã dùng để seeding.</summary>
    public Guid? PracticeAo5SnapshotId { get; set; }

    // === PLACEMENT PHASE (Giai đoạn 2) ===
    /// <summary>Số trận placement đã hoàn thành.</summary>
    public int PlacementMatchesDone { get; set; } = 0;

    /// <summary>TRUE khi đã hoàn thành đủ số trận placement → Elo hiển thị công khai.</summary>
    public bool IsPlacementComplete { get; set; } = false;

    /// <summary>Thời điểm hoàn thành placement – Elo bắt đầu xuất hiện trên Global Top Rank.</summary>
    public DateTime? PlacementCompletedAt { get; set; }

    // === K-FACTOR (Giai đoạn 2 → 3) ===
    /// <summary>
    /// Hệ số K hiện tại của người chơi này.
    /// Placement: = elo_config.k_factor_placement (cao, ví dụ 100).
    /// Standard:  = elo_config.k_factor_standard (ổn định, ví dụ 20-30).
    /// </summary>
    public int KFactorCurrent { get; set; } = 100;

    // === THỐNG KÊ ===
    public int TotalWins { get; set; } = 0;
    public int TotalLosses { get; set; } = 0;
    public int TotalDraws { get; set; } = 0;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public PuzzleType PuzzleType { get; set; } = null!;
}
