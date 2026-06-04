namespace CubeNexus.Application.DTOs.Arena;

/// <summary>Một dòng trên bảng xếp hạng Global Top Rank.</summary>
public class LeaderboardEntryDto
{
    public int Rank { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }

    /// <summary>Elo hiện tại (chỉ hiển thị với players đã hoàn thành Placement).</summary>
    public int Elo { get; set; }

    public int? PeakElo { get; set; }
    public int TotalWins { get; set; }
    public int TotalLosses { get; set; }
    public int TotalDraws { get; set; }
    public double WinRate { get; set; }
    public DateTime? PlacementCompletedAt { get; set; }
}

/// <summary>Kết quả phân trang bảng xếp hạng.</summary>
public class LeaderboardResponseDto
{
    public List<LeaderboardEntryDto> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
