namespace CubeNexus.Application.DTOs.Practice;

/// <summary>Ghi nhận 1 lần giải rubik trong session</summary>
public class SubmitSolveDto
{
    /// <summary>ID session đang tập</summary>
    public Guid SessionId { get; set; }

    /// <summary>Chuỗi tráo được dùng cho lần giải này</summary>
    public string ScrambleSequence { get; set; } = string.Empty;

    /// <summary>Thời gian giải (milliseconds)</summary>
    public int TimeMs { get; set; }

    /// <summary>
    /// Loại penalty: OK | PLUS_2 | DNF
    /// (không phân biệt hoa thường)
    /// </summary>
    public string? Penalty { get; set; }
}
