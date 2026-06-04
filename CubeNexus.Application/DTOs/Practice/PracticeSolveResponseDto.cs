namespace CubeNexus.Application.DTOs.Practice;

public class PracticeSolveResponseDto
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string ScrambleSequence { get; set; } = string.Empty;

    /// <summary>Thời gian gốc (ms), chưa cộng penalty</summary>
    public int TimeMs { get; set; }

    /// <summary>Penalty: OK | PLUS_2 | DNF</summary>
    public string? PenaltyCode { get; set; }

    /// <summary>Thời gian hiển thị sau penalty (ms). -1 nếu DNF</summary>
    public int DisplayTimeMs { get; set; }

    public DateTime SolvedAt { get; set; }

    // ─── Ao5 rolling ───────────────────────────────────────────
    /// <summary>Giá trị Ao5 sau lần giải này (null nếu chưa đủ 5 lần)</summary>
    public int? CurrentAo5Ms { get; set; }
}
