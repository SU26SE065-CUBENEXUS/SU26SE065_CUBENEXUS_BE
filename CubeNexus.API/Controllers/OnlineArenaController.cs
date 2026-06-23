using System.Security.Claims;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Application.UseCases.OnlineArena;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/online")]
[Authorize]
public class OnlineArenaController : ControllerBase
{
    private static readonly string[] AdminRoles = ["ADMIN", "MANAGER"];

    private bool TryGetCurrentUserId(out Guid userId)
    {
        userId = Guid.Empty;

        var rawUserId = User.FindFirstValue("id")
            ?? User.FindFirstValue("userId")
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return !string.IsNullOrWhiteSpace(rawUserId) && Guid.TryParse(rawUserId, out userId);
    }

    private bool IsAdminLike()
        => AdminRoles.Any(User.IsInRole);

    private IActionResult Unauthorized401()
        => Unauthorized(new
        {
            code = "UNAUTHORIZED",
            message = "Missing or invalid user id claim in token."
        });

    private IActionResult MapException(Exception ex) => ex switch
    {
        ConflictException conflict => Conflict(new { code = "CONFLICT", message = conflict.Message }),
        UnauthorizedAccessException forbidden => StatusCode(StatusCodes.Status403Forbidden, new { code = "FORBIDDEN", message = forbidden.Message }),
        KeyNotFoundException notFound => NotFound(new { code = "NOT_FOUND", message = notFound.Message }),
        InvalidOperationException invalidOperation => BadRequest(new { code = "BAD_REQUEST", message = invalidOperation.Message }),
        ArgumentException argument => BadRequest(new { code = "BAD_REQUEST", message = argument.Message }),
        _ => StatusCode(StatusCodes.Status500InternalServerError, new { code = "SERVER_ERROR", message = ex.Message })
    };

    [HttpPost("profiles/init")]
    public async Task<IActionResult> InitProfile([FromBody] InitOnlineProfileRequest request, [FromServices] InitOnlineProfileUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId, request.PuzzleTypeId));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("profiles/me")]
    public async Task<IActionResult> GetMyProfiles([FromServices] GetMyOnlineProfilesUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("leaderboard")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLeaderboard([FromQuery] Guid puzzleTypeId, [FromServices] GetOnlineLeaderboardUseCase useCase)
    {
        try
        {
            return Ok(await useCase.ExecuteAsync(puzzleTypeId));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matchmaking/find")]
    public async Task<IActionResult> FindMatch([FromBody] FindMatchRequest request, [FromServices] FindOnlineMatchUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId, request.PuzzleTypeId));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matchmaking/cancel")]
    public async Task<IActionResult> CancelMatchmaking([FromBody] FindMatchRequest request, [FromServices] CancelMatchmakingUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            await useCase.ExecuteAsync(userId, request.PuzzleTypeId);
            return Ok(new { message = "Matchmaking cancelled." });
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("matchmaking/status")]
    public async Task<IActionResult> GetMatchmakingStatus([FromQuery] Guid puzzleTypeId, [FromServices] GetMatchmakingStatusUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId, puzzleTypeId));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("matches/{matchId:guid}")]
    public async Task<IActionResult> GetMatchDetail(Guid matchId, [FromServices] GetMatchDetailUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId, matchId, IsAdminLike()));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("matches/by-room-token/{roomToken}")]
    public async Task<IActionResult> GetMatchByRoomToken(string roomToken, [FromServices] GetMatchDetailUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteByRoomTokenAsync(userId, roomToken, IsAdminLike()));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/camera-ready")]
    public async Task<IActionResult> MarkCameraReady(Guid matchId, [FromServices] MarkCameraReadyUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/webrtc-connected")]
    public async Task<IActionResult> MarkWebRtcConnected(Guid matchId, [FromBody] MarkWebRtcConnectedRequest request, [FromServices] MarkWebRtcConnectedUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, request));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/ready")]
    public async Task<IActionResult> MarkPlayerReady(Guid matchId, [FromServices] MarkPlayerReadyUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/start")]
    public async Task<IActionResult> StartMatch(Guid matchId, [FromServices] StartOnlineMatchUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/scramble-validation")]
    public async Task<IActionResult> ValidateScrambleCubeState(
        Guid matchId,
        [FromBody] CubeScanValidationRequest request,
        [FromServices] ValidateScrambleCubeStateUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, request));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/scanner/{validationType}/start")]
    public async Task<IActionResult> StartScannerSession(
        Guid matchId,
        string validationType,
        [FromServices] StartOnlineMatchScannerSessionUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, validationType));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("matches/{matchId:guid}/scanner/{validationType}")]
    public async Task<IActionResult> GetScannerSession(
        Guid matchId,
        string validationType,
        [FromServices] GetOnlineMatchScannerSessionUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, validationType));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/scanner/{validationType}/observe")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> ObserveScannerFrame(
        Guid matchId,
        string validationType,
        [FromForm] OnlineArenaScannerObserveFormRequest request,
        [FromServices] ObserveOnlineMatchScannerFrameUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            var base64 = await ReadAsBase64Async(request.Snapshot, HttpContext.RequestAborted);
            return Ok(await useCase.ExecuteAsync(matchId, userId, validationType, base64, new OnlineArenaScannerObserveRequest
            {
                ScanSessionId = request.ScanSessionId,
                ScanGeneration = request.ScanGeneration,
                RequestId = request.RequestId,
                TargetFaceIndex = request.TargetFaceIndex
            }));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/scanner/{validationType}/retry-face")]
    public async Task<IActionResult> RetryScannerFace(
        Guid matchId,
        string validationType,
        [FromServices] RetryOnlineMatchScannerFaceUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, validationType));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/scanner/{validationType}/reset")]
    public async Task<IActionResult> ResetScannerSession(
        Guid matchId,
        string validationType,
        [FromServices] ResetOnlineMatchScannerSessionUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, validationType));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/finish-validation")]
    public async Task<IActionResult> ValidateFinishCubeState(
        Guid matchId,
        [FromBody] CubeScanValidationRequest request,
        [FromServices] ValidateFinishCubeStateUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, request));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/reconcile-status")]
    public async Task<IActionResult> ReconcileMatchStatus(
        Guid matchId,
        [FromServices] ReconcileOnlineMatchStatusUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId, matchId, IsAdminLike()));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/dev/mock-finish-pass")]
    public async Task<IActionResult> MockFinishPass(
        Guid matchId,
        [FromServices] MockOnlineMatchFinishPassUseCase useCase,
        [FromServices] IWebHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
            return NotFound();
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId, matchId, IsAdminLike()));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("ai/health")]
    public async Task<IActionResult> GetAiHealth([FromServices] IAiRubikClient aiRubikClient)
    {
        var health = await aiRubikClient.GetHealthAsync();
        if (health is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "UNAVAILABLE",
                serviceName = "AiRubik",
                modelPath = string.Empty,
                modelExists = false,
                modelVersion = "unknown",
                modelLoaded = false
            });
        }

        return Ok(health);
    }

    [HttpPost("matches/{matchId:guid}/ai/pre-check")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> RunAiPreCheck(
        Guid matchId,
        [FromForm] AiImageCheckFormRequest request,
        [FromServices] RunAiRubikCheckUseCase useCase,
        [FromServices] IWebHostEnvironment environment)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            var stored = await SaveUploadedFileAsync(environment, matchId, userId, "pre-check", request.Snapshot);
            return Ok(await useCase.ExecuteAsync(matchId, userId, "PRE_CHECK", stored.Base64, stored.Path));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/ai/scramble-check")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> RunAiScrambleCheck(
        Guid matchId,
        [FromForm] AiImageCheckFormRequest request,
        [FromServices] RunAiRubikCheckUseCase useCase,
        [FromServices] IWebHostEnvironment environment)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            var stored = await SaveUploadedFileAsync(environment, matchId, userId, "scramble-check", request.Snapshot);
            return Ok(await useCase.ExecuteAsync(matchId, userId, "SCRAMBLE_CHECK", stored.Base64, stored.Path, request.ScrambleSequence));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/ai/finish-check")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> RunAiFinishCheck(
        Guid matchId,
        [FromForm] AiImageCheckFormRequest request,
        [FromServices] RunAiRubikCheckUseCase useCase,
        [FromServices] IWebHostEnvironment environment)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            var stored = await SaveUploadedFileAsync(environment, matchId, userId, "finish-check", request.Snapshot);
            return Ok(await useCase.ExecuteAsync(matchId, userId, "FINISH_CHECK", stored.Base64, stored.Path, request.ScrambleSequence));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/cancel")]
    public async Task<IActionResult> CancelMatch(Guid matchId, [FromServices] CancelActiveMatchUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, IsAdminLike()));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("mobile-timer/connect")]
    public async Task<IActionResult> ConnectMobileTimer([FromBody] ConnectMobileTimerRequest request, [FromServices] ConnectMobileTimerUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId, request.QrSessionCode, request.DeviceInfo));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("mobile-timer/disconnect")]
    public async Task<IActionResult> DisconnectMobileTimer([FromBody] DisconnectMobileTimerRequest request, [FromServices] DisconnectMobileTimerUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId, request.MatchId));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("mobile-timer/submit-result")]
    public async Task<IActionResult> SubmitResult([FromBody] SubmitOnlineResultRequest request, [FromServices] SubmitOnlineMatchResultUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId, request.MatchId, request.TimeMs, request.IsDnf));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("matches/{matchId:guid}/reports")]
    public async Task<IActionResult> CreateFraudReport(Guid matchId, [FromBody] CreateFraudReportRequest request, [FromServices] CreateFraudReportUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(userId, matchId, request));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private static async Task<StoredUpload> SaveUploadedFileAsync(IWebHostEnvironment environment, Guid matchId, Guid userId, string category, IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("Uploaded file is required.");

        var safeName = Path.GetFileName(file.FileName);
        var baseDir = Path.Combine(environment.ContentRootPath, "storage", "online-arena", matchId.ToString(), userId.ToString(), category);
        Directory.CreateDirectory(baseDir);
        var finalPath = Path.Combine(baseDir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{safeName}");

        await using var target = System.IO.File.Create(finalPath);
        await file.CopyToAsync(target);
        await target.FlushAsync();

        var bytes = await System.IO.File.ReadAllBytesAsync(finalPath);
        using var sha256 = SHA256.Create();
        var checksum = Convert.ToHexString(sha256.ComputeHash(bytes));

        return new StoredUpload
        {
            Path = finalPath,
            Base64 = Convert.ToBase64String(bytes),
            Checksum = checksum
        };
    }

    private static async Task<string> ReadAsBase64Async(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("Uploaded file is required.");

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return Convert.ToBase64String(memory.ToArray());
    }

    public class AiImageCheckFormRequest
    {
        public IFormFile Snapshot { get; set; } = null!;
        public string? ScrambleSequence { get; set; }
    }

    public class OnlineArenaScannerObserveFormRequest
    {
        public IFormFile Snapshot { get; set; } = null!;
        public string ScanSessionId { get; set; } = string.Empty;
        public int ScanGeneration { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public int TargetFaceIndex { get; set; }
    }

    private sealed class StoredUpload
    {
        public string Path { get; set; } = string.Empty;
        public string Base64 { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
    }
}
