using System.Security.Claims;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

/// <summary>
/// Giai đoạn 2 &amp; 3: Quản lý Online Arena, Placement Phase, và bảng xếp hạng.
/// </summary>
[ApiController]
[Route("api/arena")]
[Authorize(Roles = "COMPETITOR")]
public class OnlineArenaController : ControllerBase
{
    private readonly IOnlineArenaService _arenaService;

    public OnlineArenaController(IOnlineArenaService arenaService)
    {
        _arenaService = arenaService;
    }

    /// <summary>
    /// Lấy hồ sơ Online Arena của người chơi hiện tại.
    /// Elo chỉ hiển thị sau khi hoàn thành Placement Phase.
    /// </summary>
    /// <param name="puzzleTypeId">ID loại puzzle.</param>
    [HttpGet("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyProfile([FromQuery] Guid puzzleTypeId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var profile = await _arenaService.GetPlayerProfileAsync(userId.Value, puzzleTypeId);
        if (profile == null)
            return NotFound(new
            {
                message = "Chưa có Online Profile. Hãy gọi /api/elo-seeding/initialize-profile để khởi tạo.",
                nextStep = "Hoàn thành ≥5 lượt giải Practice → calculate-ao5 → initialize-profile"
            });

        return Ok(profile);
    }

    /// <summary>
    /// Kiểm tra tư cách tham gia PVP của người chơi hiện tại.
    /// Trả về trạng thái giai đoạn (NO_PROFILE / PLACEMENT / STANDARD),
    /// CanJoinPvp, lý do bị chặn và hướng dẫn bước tiếp theo.
    /// Frontend dùng endpoint này để quyết định hiển thị UI nào.
    /// </summary>
    /// <param name="puzzleTypeId">ID loại puzzle.</param>
    [HttpGet("eligibility")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyEligibility([FromQuery] Guid puzzleTypeId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _arenaService.GetPlayerEligibilityAsync(userId.Value, puzzleTypeId);
        return Ok(result);
    }

    /// <summary>
    /// Lấy hồ sơ Online Arena của một người chơi bất kỳ.
    /// </summary>
    [HttpGet("profile/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlayerProfile(Guid userId, [FromQuery] Guid puzzleTypeId)
    {
        var profile = await _arenaService.GetPlayerProfileAsync(userId, puzzleTypeId);
        if (profile == null)
            return NotFound(new { message = "Không tìm thấy Online Profile của người chơi này." });

        return Ok(profile);
    }

    /// <summary>
    /// Lấy bảng xếp hạng Global Top Rank.
    /// Chỉ hiển thị players đã hoàn thành Placement Phase (5 trận).
    /// Sắp xếp theo Elo giảm dần.
    /// </summary>
    /// <param name="puzzleTypeId">ID loại puzzle.</param>
    /// <param name="page">Trang hiện tại (bắt đầu từ 1).</param>
    /// <param name="pageSize">Số dòng mỗi trang (tối đa 100).</param>
    [HttpGet("leaderboard")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] Guid puzzleTypeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _arenaService.GetLeaderboardAsync(puzzleTypeId, page, pageSize);
        return Ok(result);
    }

    /// <summary>
    /// Ghi nhận kết quả trận đấu và cập nhật Elo cho cả 2 người chơi.
    /// Công thức: R' = R + K * (S - E).
    /// Sau trận thứ 5 của Placement: K tự động hạ về k_factor_standard.
    /// </summary>
    /// <param name="matchId">ID trận đấu đã kết thúc.</param>
    /// <param name="winnerId">ID người thắng. Bỏ trống nếu hòa.</param>
    [HttpPost("match/{matchId:guid}/result")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordMatchResult(
        Guid matchId,
        [FromQuery] Guid? winnerId = null)
    {
        try
        {
            var result = await _arenaService.RecordMatchResultAsync(matchId, winnerId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out var id) ? id : null;
    }
}
