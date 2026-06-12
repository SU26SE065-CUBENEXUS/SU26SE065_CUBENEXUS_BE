using System.Security.Claims;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/arena")]
public class OnlineArenaController : ControllerBase
{
    private readonly IOnlineArenaService _arenaService;

    public OnlineArenaController(IOnlineArenaService arenaService)
    {
        _arenaService = arenaService;
    }

    [HttpGet("profile")]
    [Authorize(Roles = "COMPETITOR")]
    public async Task<IActionResult> GetMyProfile([FromQuery] Guid puzzleTypeId)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new { message = "Token không chứa userId hợp lệ." });

        var profile = await _arenaService.GetPlayerProfileAsync(userId.Value, puzzleTypeId);

        if (profile == null)
        {
            return NotFound(new
            {
                message = "Chưa có Online Profile. Hãy gọi /api/elo-seeding/initialize-profile để khởi tạo.",
                nextStep = "Hoàn thành ≥5 lượt giải Practice → calculate-ao5 → initialize-profile"
            });
        }

        return Ok(profile);
    }

    [HttpGet("eligibility")]
    [Authorize(Roles = "COMPETITOR")]
    public async Task<IActionResult> GetMyEligibility([FromQuery] Guid puzzleTypeId)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new { message = "Token không chứa userId hợp lệ." });

        var result = await _arenaService.GetPlayerEligibilityAsync(userId.Value, puzzleTypeId);
        return Ok(result);
    }

    [HttpGet("profile/{userId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPlayerProfile(
        Guid userId,
        [FromQuery] Guid puzzleTypeId)
    {
        var profile = await _arenaService.GetPlayerProfileAsync(userId, puzzleTypeId);

        if (profile == null)
            return NotFound(new { message = "Không tìm thấy Online Profile của người chơi này." });

        return Ok(profile);
    }

    [HttpGet("leaderboard")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] Guid puzzleTypeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _arenaService.GetLeaderboardAsync(puzzleTypeId, page, pageSize);
        return Ok(result);
    }

    [HttpPost("match/{matchId:guid}/result")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> RecordMatchResult(
        Guid matchId,
        [FromQuery] Guid? winnerId = null)
    {
        try
        {
            var result = await _arenaService.RecordMatchResultAsync(matchId, winnerId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}