namespace CubeNexus.Application.DTOs.Arena;

public class OnlineProfileDto
{
    public Guid UserId { get; set; }

    /// <summary>Elo Standard công khai. NULL khi chưa hoàn thành Placement.</summary>
    public int? EloStandardVisible { get; set; }

    public int PeakEloStandard { get; set; }

    /// <summary>Elo Medley — NULL cho đến khi có chế độ Medley.</summary>
    public int? EloMedley { get; set; }

    public int PlacementMatchesDoneStandard { get; set; }
    public int PlacementMatchCount { get; set; }
    public bool IsPlacementCompleteStandard { get; set; }
    public DateTime? PlacementCompletedAtStandard { get; set; }

    public int TotalWinsStandard { get; set; }
    public int TotalLossesStandard { get; set; }
    public int TotalDrawsStandard { get; set; }
    public double WinRate { get; set; }

    public DateTime CreatedAt { get; set; }
}
