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
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new { message = "Token không chứa userId hợp lệ." });

        var profile = await _arenaService.GetPlayerProfileAsync(userId.Value);

        if (profile == null)
            return NotFound(new { message = "Chưa có Online Profile." });

        return Ok(profile);
    }

    [HttpGet("eligibility")]
    [Authorize(Roles = "COMPETITOR")]
    public async Task<IActionResult> GetMyEligibility()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new { message = "Token không chứa userId hợp lệ." });

        var result = await _arenaService.GetPlayerEligibilityAsync(userId.Value);
        return Ok(result);
    }

    [HttpGet("profile/{userId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPlayerProfile(Guid userId)
    {
        var profile = await _arenaService.GetPlayerProfileAsync(userId);

        if (profile == null)
            return NotFound(new { message = "Không tìm thấy Online Profile của người chơi này." });

        return Ok(profile);
    }

    [HttpGet("leaderboard")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _arenaService.GetLeaderboardAsync(page, pageSize);
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
