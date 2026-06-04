namespace CubeNexus.Domain.Entities;

public class EloSeedThreshold
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }

    /// <summary>
    /// Nhãn mô tả ngưỡng, ví dụ: "Dưới 15 giây", "20-30 giây", "60 giây trở lên".
    /// </summary>
    public string? Label { get; set; }

    /// <summary>Thời gian Ao5 tối thiểu (ms). NULL = không giới hạn dưới.</summary>
    public int? MinTimeMs { get; set; }

    /// <summary>Thời gian Ao5 tối đa (ms). NULL = không giới hạn trên (ngưỡng cuối).</summary>
    public int? MaxTimeMs { get; set; }

    /// <summary>Điểm Elo seeding tương ứng với ngưỡng này.</summary>
    public int EloValue { get; set; }

    /// <summary>Thứ tự ưu tiên kiểm tra (từ nhỏ đến lớn, ưu tiên ngưỡng thấp nhất trước).</summary>
    public int SortOrder { get; set; }

    public PuzzleType PuzzleType { get; set; } = null!;
}
