namespace CubeNexus.Domain.Entities;

public class OnlineProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    // === ELO STANDARD (online PVP 1v1 — dùng chung mọi puzzle) ===
    public int EloStandard { get; set; } = 1000;
    public int PeakEloStandard { get; set; } = 1000;
    public int PlacementMatchesDoneStandard { get; set; } = 0;
    public bool IsPlacementCompleteStandard { get; set; } = false;
    public DateTime? PlacementCompletedAtStandard { get; set; }
    public int KFactorCurrentStandard { get; set; } = 100;
    public int TotalWinsStandard { get; set; } = 0;
    public int TotalLossesStandard { get; set; } = 0;
    public int TotalDrawsStandard { get; set; } = 0;

    // === ELO MEDLEY (dự phòng — chưa kích hoạt) ===
    public int? EloMedley { get; set; }
    public int? PeakEloMedley { get; set; }
    public int PlacementMatchesDoneMedley { get; set; } = 0;
    public bool IsPlacementCompleteMedley { get; set; } = false;
    public DateTime? PlacementCompletedAtMedley { get; set; }
    public int? KFactorCurrentMedley { get; set; }
    public int TotalWinsMedley { get; set; } = 0;
    public int TotalLossesMedley { get; set; } = 0;
    public int TotalDrawsMedley { get; set; } = 0;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
