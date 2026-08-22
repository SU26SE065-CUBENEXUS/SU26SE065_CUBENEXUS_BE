using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Infrastructure.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/dev/ai/scanner-test")]
[AllowAnonymous]
public class AiScannerTestController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly AiRubikOptions _options;

    public AiScannerTestController(IWebHostEnvironment environment, IOptions<AiRubikOptions> options)
    {
        _environment = environment;
        _options = options.Value;
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth([FromServices] IAiRubikClient aiRubikClient, CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        var health = await aiRubikClient.GetHealthAsync(cancellationToken);
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

    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession([FromServices] IAiRubikClient aiRubikClient, CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        try
        {
            return Ok(await aiRubikClient.StartScannerTestSessionAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                sessionId = string.Empty,
                scannerState = "AI_UNAVAILABLE",
                reason = $"Không thể khởi tạo session với AI Service: {ex.Message}",
                capturedFaceCount = 0,
                faces = Array.Empty<object>()
            });
        }
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId, [FromServices] IAiRubikClient aiRubikClient, CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        try
        {
            return Ok(await aiRubikClient.GetScannerTestSessionAsync(sessionId, cancellationToken));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                sessionId,
                scannerState = "AI_UNAVAILABLE",
                reason = $"Không thể lấy thông tin session: {ex.Message}"
            });
        }
    }

    [HttpPost("sessions/{sessionId}/preview")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> PreviewFrame(
        string sessionId,
        [FromForm] ScannerFrameFormRequest request,
        [FromServices] IAiRubikClient aiRubikClient,
        CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        try
        {
            var imageBytes = await ReadAsBytesAsync(request.Snapshot, cancellationToken);
            return Ok(await aiRubikClient.PreviewScannerTestFrameAsync(sessionId, imageBytes, request.Snapshot.FileName, request.Snapshot.ContentType, BuildScannerMetadata(request), cancellationToken));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                sessionId,
                scannerState = "AI_UNAVAILABLE",
                reason = $"AI Preview gặp lỗi: {ex.Message}"
            });
        }
    }

    [HttpPost("sessions/{sessionId}/observe")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> ObserveFrame(
        string sessionId,
        [FromForm] ScannerFrameFormRequest request,
        [FromServices] IAiRubikClient aiRubikClient,
        CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        try
        {
            var imageBytes = await ReadAsBytesAsync(request.Snapshot, cancellationToken);
            return Ok(await aiRubikClient.ObserveScannerTestFrameAsync(sessionId, imageBytes, request.Snapshot.FileName, request.Snapshot.ContentType, BuildScannerMetadata(request), cancellationToken));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                sessionId,
                scannerState = "AI_UNAVAILABLE",
                reason = $"AI Observe gặp lỗi: {ex.Message}",
                stickers = Array.Empty<object>(),
                stableFrames = 0,
                requiredStableFrames = 3
            });
        }
    }

    [HttpPost("sessions/{sessionId}/scan-face")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> ScanFace(
        string sessionId,
        [FromForm] ScannerFaceBatchFormRequest request,
        [FromServices] IAiRubikClient aiRubikClient,
        CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        if (request.Frames is null || request.Frames.Count == 0)
            return BadRequest(new { code = "BAD_REQUEST", message = "At least one frame is required." });

        try
        {
            var framesBase64 = new List<string>(request.Frames.Count);
            foreach (var frame in request.Frames)
            {
                framesBase64.Add(await ReadAsBase64Async(frame, cancellationToken));
            }

            return Ok(await aiRubikClient.ScanScannerTestFaceAsync(sessionId, framesBase64, cancellationToken));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                sessionId,
                scannerState = "AI_UNAVAILABLE",
                reason = $"AI Scan Face gặp lỗi: {ex.Message}"
            });
        }
    }

    [HttpPost("sessions/{sessionId}/retry-face")]
    public async Task<IActionResult> RetryFace(string sessionId, [FromServices] IAiRubikClient aiRubikClient, CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        try
        {
            return Ok(await aiRubikClient.RetryScannerTestFaceAsync(sessionId, cancellationToken));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                sessionId,
                scannerState = "AI_UNAVAILABLE",
                reason = $"AI Retry Face gặp lỗi: {ex.Message}"
            });
        }
    }

    [HttpPost("sessions/{sessionId}/reset")]
    public async Task<IActionResult> ResetSession(string sessionId, [FromServices] IAiRubikClient aiRubikClient, CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        try
        {
            return Ok(await aiRubikClient.ResetScannerTestSessionAsync(sessionId, cancellationToken));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                sessionId,
                scannerState = "AI_UNAVAILABLE",
                reason = $"AI Reset Session gặp lỗi: {ex.Message}"
            });
        }
    }

    private IActionResult? EnsureEnabled()
    {
        if (!_options.EnableUnauthenticatedScannerTest)
            return NotFound();
        return null;
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

    private static async Task<byte[]> ReadAsBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("Uploaded file is required.");

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private static Dictionary<string, object?> BuildScannerMetadata(ScannerFrameFormRequest request)
    {
        return new Dictionary<string, object?>
        {
            ["source"] = "cubenexus-api",
            ["scanSessionId"] = request.ScanSessionId,
            ["scanGeneration"] = request.ScanGeneration,
            ["requestId"] = request.RequestId,
            ["targetFaceIndex"] = request.TargetFaceIndex
        };
    }

    public class ScannerFrameFormRequest
    {
        public IFormFile Snapshot { get; set; } = null!;
        public string? ScanSessionId { get; set; }
        public int ScanGeneration { get; set; }
        public string? RequestId { get; set; }
        public int TargetFaceIndex { get; set; }
    }

    public class ScannerFaceBatchFormRequest
    {
        public List<IFormFile> Frames { get; set; } = [];
    }
}
