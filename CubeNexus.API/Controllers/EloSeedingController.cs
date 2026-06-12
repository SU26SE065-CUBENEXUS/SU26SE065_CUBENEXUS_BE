using System.Security.Claims;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/elo-seeding")]
[Authorize(Roles = "COMPETITOR")]
public class EloSeedingController : ControllerBase
{
    private const int PlacementMatchCount = 5;

    private readonly IEloSeedingService _seedingService;

    public EloSeedingController(IEloSeedingService seedingService)
    {
        _seedingService = seedingService;
    }

    [HttpGet("practice-status")]
    public async Task<IActionResult> GetPracticeStatus([FromQuery] Guid puzzleTypeId)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new { message = "Token không chứa userId hợp lệ." });

        var status = await _seedingService.GetPracticeStatusAsync(userId.Value, puzzleTypeId);
        return Ok(status);
    }

    [HttpPost("calculate-ao5")]
    public async Task<IActionResult> CalculateAo5([FromQuery] Guid puzzleTypeId)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new { message = "Token không chứa userId hợp lệ." });

        var snapshot = await _seedingService.CalculateAndSaveAo5Async(userId.Value, puzzleTypeId);

        if (snapshot == null)
        {
            return BadRequest(new
            {
                message = "Chưa đủ số lượt giải Practice để tính Ao5.",
                nextStep = "Gọi GET /api/elo-seeding/practice-status để biết còn thiếu bao nhiêu lượt."
            });
        }

        return Ok(new
        {
            snapshot.Id,
            snapshot.Ao5TimeMs,
            Ao5Display = $"{snapshot.Ao5TimeMs / 1000.0:F2}s",
            snapshot.AssignedElo,
            snapshot.CalculatedAt
        });
    }

    [HttpPost("initialize-profile")]
    public async Task<IActionResult> InitializeProfile([FromQuery] Guid puzzleTypeId)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new { message = "Token không chứa userId hợp lệ." });

        try
        {
            var profile = await _seedingService.InitializeOnlineProfileAsync(userId.Value, puzzleTypeId);

            return StatusCode(StatusCodes.Status201Created, new
            {
                profile.Id,
                profile.UserId,
                profile.PuzzleTypeId,
                SeedElo = profile.Elo,
                SeedSourceCode = profile.SeedSourceCode,
                PracticeAo5Ms = profile.PracticeAo5Ms,
                Ao5Display = profile.PracticeAo5Ms.HasValue
                    ? $"{profile.PracticeAo5Ms.Value / 1000.0:F2}s"
                    : null,
                profile.KFactorCurrent,
                PlacementMatchesRemaining = PlacementMatchCount,
                CurrentStage = "PLACEMENT",
                message = $"Online Profile đã được khởi tạo. Elo seeding: {profile.Elo}. Hãy hoàn thành {PlacementMatchCount} trận PVP để Elo được công khai.",
                nextStep = "Vào Online Arena để tìm trận PVP."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message,
                nextStep = ex.Message.Contains("calculate-ao5")
                    ? "Gọi POST /api/elo-seeding/calculate-ao5 trước để tính Ao5 seeding."
                    : "Gọi POST /api/practice/solves để thêm lượt giải tập luyện."
            });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}