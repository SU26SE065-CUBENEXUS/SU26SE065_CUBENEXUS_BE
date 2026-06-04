namespace CubeNexus.Application.DTOs.Arena;

/// <summary>
/// Kết quả kiểm tra tư cách tham gia PVP Online Arena của người chơi.
/// Được trả về bởi GET /api/arena/eligibility.
/// </summary>
public class PlayerEligibilityDto
{
    public Guid UserId { get; set; }
    public Guid PuzzleTypeId { get; set; }

    // === KẾT QUẢ CHÍNH ===

    /// <summary>
    /// TRUE khi người chơi có thể vào hàng đợi matchmaking PVP.
    /// Điều kiện: đã hoàn thành Practice Ao5 seeding (có OnlineProfile).
    /// </summary>
    public bool CanJoinPvp { get; set; }

    /// <summary>
    /// Lý do bị chặn. NULL nếu CanJoinPvp = true.
    /// Ví dụ: "Bạn cần hoàn thành ít nhất 5 lượt giải Practice và tính Ao5 trước khi tham gia PVP."
    /// </summary>
    public string? BlockReason { get; set; }

    // === TRẠNG THÁI CHI TIẾT ===

    /// <summary>Đã khởi tạo Online Profile (hoàn thành seeding) chưa.</summary>
    public bool HasOnlineProfile { get; set; }

    /// <summary>Đã hoàn thành 5 trận Placement → Elo công khai chưa.</summary>
    public bool IsPlacementComplete { get; set; }

    /// <summary>Số trận Placement đã hoàn thành.</summary>
    public int PlacementMatchesDone { get; set; }

    /// <summary>Tổng số trận Placement cần thiết (từ EloConfig).</summary>
    public int PlacementMatchCount { get; set; }

    /// <summary>Elo "ẩn" hiện tại (chỉ hiển thị khi đang Placement). NULL nếu chưa seeding.</summary>
    public int? HiddenElo { get; set; }

    /// <summary>Elo "thật" công khai. NULL nếu chưa hoàn thành Placement.</summary>
    public int? PublicElo { get; set; }

    // === STAGE ===

    /// <summary>
    /// Giai đoạn hiện tại:
    /// "NO_PROFILE"  – chưa seeding xong, không thể vào PVP.
    /// "PLACEMENT"   – đang 5 trận đầu, Elo ẩn, được vào PVP.
    /// "STANDARD"    – đã placed, Elo công khai, được vào PVP.
    /// </summary>
    public string CurrentStage { get; set; } = "NO_PROFILE";

    /// <summary>Hướng dẫn bước tiếp theo cho người chơi.</summary>
    public string NextStepHint { get; set; } = string.Empty;
}
