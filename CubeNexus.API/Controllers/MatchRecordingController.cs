using System.Security.Claims;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.UseCases.OnlineArena;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/matches")]
[Authorize]
public class MatchRecordingController : ControllerBase
{
    private static readonly string[] AdminRoles = ["ADMIN", "MANAGER"];

    [HttpPost("{matchId:guid}/recording/started")]
    public async Task<IActionResult> MarkRecordingStarted(
        Guid matchId,
        [FromBody] MarkVideoRecordingStartedRequest request,
        [FromServices] MarkVideoRecordingStartedUseCase useCase)
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

    [HttpPost("{matchId:guid}/recording/upload-url")]
    public async Task<IActionResult> CreateUploadUrl(
        Guid matchId,
        [FromBody] CreateMatchRecordingUploadUrlRequest request,
        [FromServices] CreateMatchRecordingUploadUrlUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, request, cancellationToken));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{matchId:guid}/recording/complete")]
    public async Task<IActionResult> CompleteUpload(
        Guid matchId,
        [FromBody] CompleteMatchRecordingUploadRequest request,
        [FromServices] CompleteMatchRecordingUploadUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, request, cancellationToken));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{matchId:guid}/recording/upload-direct")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<IActionResult> UploadDirect(
        Guid matchId,
        IFormFile file,
        [FromQuery] double? durationSeconds,
        [FromServices] UploadDirectMatchRecordingUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();
        if (file == null || file.Length == 0) return BadRequest(new { code = "BAD_REQUEST", message = "No video file provided." });

        try
        {
            using var stream = file.OpenReadStream();
            return Ok(await useCase.ExecuteAsync(matchId, userId, stream, file.ContentType, durationSeconds, cancellationToken));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{matchId:guid}/recording/playback-url")]
    public async Task<IActionResult> GetPlaybackUrls(
        Guid matchId,
        [FromServices] GetMatchRecordingPlaybackUrlUseCase useCase,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(matchId, userId, IsAdminLike(), cancellationToken));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

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
}
