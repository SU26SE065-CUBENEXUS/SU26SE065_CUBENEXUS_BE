using System.Security.Claims;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

/// <summary>
/// Giai đoạn 1: Quản lý Practice Seeding và khởi tạo Online Profile.
/// </summary>
[ApiController]
[Route("api/elo-seeding")]
[Authorize(Roles = "COMPETITOR")]
public class EloSeedingController : ControllerBase
{
    private readonly IEloSeedingService _seedingService;

    public EloSeedingController(IEloSeedingService seedingService)
    {
        _seedingService = seedingService;
    }

    /// <summary>
    /// Kiểm tra trạng thái Practice seeding của người chơi hiện tại.
    /// Trả về số lượt giải có, có đủ điều kiện seeding chưa, Elo dự kiến.
    /// </summary>
    /// <param name="puzzleTypeId">ID loại puzzle (ví dụ 3x3).</param>
    [HttpGet("practice-status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPracticeStatus([FromQuery] Guid puzzleTypeId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var status = await _seedingService.GetPracticeStatusAsync(userId.Value, puzzleTypeId);
        return Ok(status);
    }

    /// <summary>
    /// Tính Ao5 từ các lượt giải Practice và lưu snapshot.
    /// Yêu cầu đủ số lượt giải tối thiểu (min_practice_solves từ elo_config).
    /// </summary>
    /// <param name="puzzleTypeId">ID loại puzzle.</param>
    [HttpPost("calculate-ao5")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CalculateAo5([FromQuery] Guid puzzleTypeId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var snapshot = await _seedingService.CalculateAndSaveAo5Async(userId.Value, puzzleTypeId);

        if (snapshot == null)
            return BadRequest(new
            {
                message = "Chưa đủ số lượt giải Practice để tính Ao5. Hãy kiểm tra /practice-status để biết số lượt cần thiết."
            });

        return Ok(new
        {
            snapshot.Id,
            snapshot.Ao5TimeMs,
            Ao5Display = $"{snapshot.Ao5TimeMs / 1000.0:F2}s",
            snapshot.AssignedElo,
            snapshot.CalculatedAt
        });
    }

    /// <summary>
    /// Khởi tạo Online Profile cho người chơi.
    /// BẮT BUỘC phải có Practice Ao5 Snapshot trước (gọi /calculate-ao5 trước).
    /// Nếu chưa có snapshot → trả về 400 với hướng dẫn cụ thể.
    /// </summary>
    /// <param name="puzzleTypeId">ID loại puzzle.</param>
    [HttpPost("initialize-profile")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> InitializeProfile([FromQuery] Guid puzzleTypeId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var profile = await _seedingService.InitializeOnlineProfileAsync(userId.Value, puzzleTypeId);

            return StatusCode(StatusCodes.Status201Created, new
            {
                profile.Id,
                profile.UserId,
                profile.PuzzleTypeId,
                SeedElo        = profile.Elo,
                SeedSourceCode = profile.SeedSourceCode,
                PracticeAo5Ms  = profile.PracticeAo5Ms,
                Ao5Display     = profile.PracticeAo5Ms.HasValue
                    ? $"{profile.PracticeAo5Ms.Value / 1000.0:F2}s"
                    : null,
                profile.KFactorCurrent,
                PlacementMatchesRemaining = 5,
                CurrentStage  = "PLACEMENT",
                message       = $"🎉 Online Profile đã được khởi tạo! " +
                                $"Elo seeding: {profile.Elo} (từ Ao5 Practice). " +
                                $"Elo của bạn đang ẩn – hãy hoàn thành 5 trận PVP để Elo được công khai trên bảng xếp hạng.",
                nextStep      = "Vào hàng đợi matchmaking tại /api/arena/queue để tìm đối thủ."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message  = ex.Message,
                nextStep = ex.Message.Contains("calculate-ao5")
                    ? "Gọi POST /api/elo-seeding/calculate-ao5 trước để tính Ao5 seeding."
                    : "Gọi POST /api/practice/solves để thêm lượt giải tập luyện."
            });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out var id) ? id : null;
    }
}
