namespace CubeNexus.Domain.Entities;

public class EloConfig
{
    public Guid Id { get; set; }

    /// <summary>Hệ số K trong giai đoạn Placement Phase (cao, ví dụ 100).</summary>
    public int KFactorPlacement { get; set; } = 100;

    /// <summary>Hệ số K sau khi hoàn thành placement (ổn định, ví dụ 20-30).</summary>
    public int KFactorStandard { get; set; } = 20;

    /// <summary>Số trận Placement bắt buộc trước khi Elo được công khai (mặc định 5).</summary>
    public int PlacementMatchCount { get; set; } = 5;

    /// <summary>Elo mặc định nếu không có dữ liệu Practice seeding.</summary>
    public int DefaultElo { get; set; } = 1000;

    /// <summary>Số lượt giải Practice tối thiểu để tính Ao5 seeding (mặc định 5).</summary>
    public int MinPracticeSolves { get; set; } = 5;

    public Guid? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User? UpdatedByUser { get; set; }
}
