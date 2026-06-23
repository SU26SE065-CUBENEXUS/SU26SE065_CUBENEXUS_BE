namespace CubeNexus.Application.DTOs.Arena;

public class PlayerEligibilityDto
{
    public Guid UserId { get; set; }

    /// <summary>TRUE ngay sau khi đăng ký — không cần Practice.</summary>
    public bool CanJoinPvp { get; set; }

    public string? BlockReason { get; set; }

    public bool HasOnlineProfile { get; set; }

    public bool IsPlacementCompleteStandard { get; set; }
    public int PlacementMatchesDoneStandard { get; set; }
    public int PlacementMatchCount { get; set; }

    /// <summary>Elo Standard ẩn khi đang Placement.</summary>
    public int? HiddenEloStandard { get; set; }

    /// <summary>Elo Standard công khai sau Placement.</summary>
    public int? PublicEloStandard { get; set; }

    /// <summary>"PLACEMENT" hoặc "STANDARD".</summary>
    public string CurrentStage { get; set; } = "PLACEMENT";

    public string NextStepHint { get; set; } = string.Empty;
}
