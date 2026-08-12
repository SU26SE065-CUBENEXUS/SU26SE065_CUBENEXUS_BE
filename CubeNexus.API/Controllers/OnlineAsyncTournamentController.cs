using CubeNexus.Application.DTOs;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/tournaments/online-async")]
public class OnlineAsyncTournamentController : ControllerBase
{
    private readonly IOnlineAsyncTournamentService _asyncTournamentService;

    public OnlineAsyncTournamentController(IOnlineAsyncTournamentService asyncTournamentService)
    {
        _asyncTournamentService = asyncTournamentService;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        if (Guid.TryParse(claim, out var userId))
            return userId;

        throw new UnauthorizedAccessException("User identity claim is invalid or missing.");
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateTournament([FromBody] CreateOnlineAsyncTournamentRequest request, CancellationToken ct)
    {
        var managerUserId = GetUserId();
        var result = await _asyncTournamentService.CreateTournamentAsync(managerUserId, request, ct);
        return CreatedAtAction(nameof(GetTournamentById), new { id = result.Id }, result);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ListTournaments([FromQuery] string? status, CancellationToken ct)
    {
        Guid? userId = null;
        try { userId = GetUserId(); } catch { }

        var list = await _asyncTournamentService.ListTournamentsAsync(status, userId, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTournamentById(Guid id, CancellationToken ct)
    {
        Guid? userId = null;
        try { userId = GetUserId(); } catch { }

        var tournament = await _asyncTournamentService.GetTournamentByIdAsync(id, userId, ct);
        return Ok(tournament);
    }

    [HttpPost("{id:guid}/register")]
    [Authorize]
    public async Task<IActionResult> RegisterCompetitor(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var success = await _asyncTournamentService.RegisterCompetitorAsync(id, userId, ct);
        return Ok(new { success, message = "Successfully registered for online tournament." });
    }

    [HttpPost("{id:guid}/attempts/start")]
    [Authorize]
    public async Task<IActionResult> StartAttempt(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _asyncTournamentService.StartAttemptAsync(id, userId, ct);
        return Ok(result);
    }

    [HttpPost("attempts/{attemptId:guid}/verify-scramble")]
    [Authorize]
    public async Task<IActionResult> VerifyScramble(Guid attemptId, [FromBody] VerifyAsyncScrambleRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await _asyncTournamentService.VerifyScrambleAsync(attemptId, userId, request, ct);
        return Ok(result);
    }

    [HttpGet("attempts/{attemptId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetAttemptState(Guid attemptId, CancellationToken ct)
        => Ok(await _asyncTournamentService.GetAttemptStateAsync(attemptId, GetUserId(), ct));

    [HttpPost("attempts/{attemptId:guid}/start-solve")]
    [Authorize]
    public async Task<IActionResult> StartSolveTimer(Guid attemptId, [FromBody] StartAsyncSolveTimerRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        request.AttemptId = attemptId;
        var result = await _asyncTournamentService.StartSolveTimerAsync(attemptId, userId, request, ct);
        return Ok(result);
    }

    [HttpPost("attempts/{attemptId:guid}/finish-solve")]
    [Authorize]
    public async Task<IActionResult> FinishSolveTimer(Guid attemptId, [FromBody] FinishAsyncSolveTimerRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        request.AttemptId = attemptId;
        var result = await _asyncTournamentService.FinishSolveTimerAsync(attemptId, userId, request, ct);
        return Ok(result);
    }

    [HttpPost("attempts/{attemptId:guid}/verify-finish")]
    [Authorize]
    public async Task<IActionResult> VerifyFinish(Guid attemptId, [FromBody] VerifyAsyncFinishRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        request.AttemptId = attemptId;
        var result = await _asyncTournamentService.VerifyFinishAsync(attemptId, userId, request, ct);
        return Ok(result);
    }

    [HttpPost("attempts/{attemptId:guid}/video")]
    [Authorize]
    [RequestSizeLimit(250_000_000)]
    public async Task<IActionResult> UploadVideo(Guid attemptId, IFormFile video, CancellationToken ct)
    {
        if (video is null || video.Length == 0)
            return BadRequest(new { message = "A non-empty video file is required." });
        await using var stream = video.OpenReadStream();
        var result = await _asyncTournamentService.UploadVideoEvidenceAsync(attemptId, GetUserId(), stream, video.ContentType ?? "video/webm", ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/reviews")]
    [Authorize]
    public async Task<IActionResult> GetAttemptsForReview(Guid id, CancellationToken ct)
    {
        var list = await _asyncTournamentService.GetAttemptsForReviewAsync(id, GetUserId(), ct);
        return Ok(list);
    }

    [HttpGet("attempts/{attemptId:guid}/video-playback")]
    [Authorize]
    public async Task<IActionResult> GetVideoPlayback(Guid attemptId, CancellationToken ct)
        => Ok(new { url = await _asyncTournamentService.GetVideoPlaybackUrlAsync(attemptId, GetUserId(), ct) });

    [HttpPut("attempts/{attemptId:guid}/review")]
    [Authorize]
    public async Task<IActionResult> ReviewAttempt(Guid attemptId, [FromBody] ReviewAsyncAttemptRequest request, CancellationToken ct)
    {
        var reviewerUserId = GetUserId();
        request.AttemptId = attemptId;
        var result = await _asyncTournamentService.ReviewAttemptAsync(attemptId, reviewerUserId, request, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/leaderboard")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLeaderboard(Guid id, CancellationToken ct)
    {
        var list = await _asyncTournamentService.GetLeaderboardAsync(id, ct);
        return Ok(list);
    }
}
