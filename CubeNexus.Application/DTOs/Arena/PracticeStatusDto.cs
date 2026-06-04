namespace CubeNexus.Application.DTOs.Arena;

/// <summary>Trạng thái Practice seeding của người chơi.</summary>
public class PracticeStatusDto
{
    // === PRACTICE (Giai đoạn 1) ===

    /// <summary>Số lượt giải Practice hợp lệ đã có.</summary>
    public int SolvesCount { get; set; }

    /// <summary>Số lượt tối thiểu cần có để tính Ao5 seeding.</summary>
    public int RequiredSolves { get; set; }

    /// <summary>Người chơi đã đủ điều kiện seeding (≥ MinPracticeSolves) chưa.</summary>
    public bool IsEligibleForSeeding { get; set; }

    /// <summary>
    /// TRUE khi đã tính Ao5 + có thể gọi /initialize-profile.
    /// (IsEligibleForSeeding = true VÀ chưa có profile).
    /// </summary>
    public bool CanInitializeProfile { get; set; }

    /// <summary>Ao5 gần nhất đã tính (ms). NULL nếu chưa đủ.</summary>
    public int? LatestAo5Ms { get; set; }

    /// <summary>Ao5 gần nhất định dạng human-readable (ví dụ "23.45s").</summary>
    public string? LatestAo5Display { get; set; }

    /// <summary>Elo seeding dự kiến dựa trên Ao5 hiện tại.</summary>
    public int? ExpectedSeedElo { get; set; }

    // === ONLINE PROFILE / PLACEMENT (Giai đoạn 2) ===

    /// <summary>Người chơi đã có online profile chưa (đã hoàn thành seeding).</summary>
    public bool HasOnlineProfile { get; set; }

    /// <summary>
    /// TRUE khi đã có online profile → được phép vào hàng đợi PVP.
    /// FALSE nếu chưa seeding xong.
    /// </summary>
    public bool CanJoinPvp { get; set; }

    /// <summary>Số trận Placement đã hoàn thành (0–PlacementMatchCount).</summary>
    public int PlacementMatchesDone { get; set; }

    /// <summary>Số trận Placement cần hoàn thành để Elo được công khai.</summary>
    public int PlacementMatchCount { get; set; }

    /// <summary>TRUE khi đã hoàn thành đủ 5 trận Placement → Elo "thật" hiển thị.</summary>
    public bool IsPlacementComplete { get; set; }

    // === STAGE INDICATOR ===

    /// <summary>
    /// Giai đoạn hiện tại của người chơi:
    /// "PRACTICE"  – chưa seeding xong (chưa có profile).
    /// "PLACEMENT" – đang trong 5 trận placement đầu (Elo ẩn).
    /// "STANDARD"  – đã hoàn thành placement (Elo công khai).
    /// </summary>
    public string CurrentStage { get; set; } = "PRACTICE";

    /// <summary>Mô tả human-readable hướng dẫn bước tiếp theo cho người chơi.</summary>
    public string NextStepHint { get; set; } = string.Empty;
}
