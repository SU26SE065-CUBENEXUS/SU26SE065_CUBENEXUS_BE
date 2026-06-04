namespace CubeNexus.Application.DTOs.Practice;

/// <summary>Thống kê tổng hợp của session sau khi kết thúc</summary>
public class PracticeSessionSummaryDto
{
    public Guid SessionId { get; set; }
    public string PuzzleTypeCode { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }

    public int TotalSolves { get; set; }
    public int DnfCount { get; set; }

    /// <summary>Thời gian trung bình (ms), không tính DNF</summary>
    public int? MeanMs { get; set; }

    /// <summary>Thời gian tốt nhất (ms), không tính DNF</summary>
    public int? BestMs { get; set; }

    /// <summary>Ao5 tốt nhất trong session (ms)</summary>
    public int? BestAo5Ms { get; set; }

    public IReadOnlyList<PracticeSolveResponseDto> Solves { get; set; } = [];
}
