namespace CubeNexus.Domain.Entities;

/// <summary>
/// Lưu snapshot Ao5 từ chế độ Practice dùng để seeding Elo Online Arena.
/// Được tạo khi người chơi hoàn thành đủ số lượt giải tối thiểu (min_practice_solves).
/// Tính Ao5 theo chuẩn WCA: loại best + worst, lấy trung bình 3 lượt giữa.
/// </summary>
public class PracticeAo5Snapshot
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PuzzleTypeId { get; set; }

    /// <summary>Giá trị Ao5 tính được (ms), đã áp dụng luật WCA loại best/worst.</summary>
    public int Ao5TimeMs { get; set; }

    /// <summary>Elo seeding được gán dựa trên ngưỡng elo_seed_thresholds.</summary>
    public int AssignedElo { get; set; }

    /// <summary>Ngưỡng đã áp dụng để gán Elo (FK tới elo_seed_thresholds).</summary>
    public Guid? SeedThresholdId { get; set; }

    /// <summary>Thời điểm snapshot được tính toán và lưu.</summary>
    public DateTime CalculatedAt { get; set; }

    /// <summary>TRUE khi snapshot này đã dùng để khởi tạo online_profile.</summary>
    public bool IsUsedForSeeding { get; set; } = false;

    // Navigation properties
    public User User { get; set; } = null!;
    public PuzzleType PuzzleType { get; set; } = null!;
    public EloSeedThreshold? SeedThreshold { get; set; }
}
