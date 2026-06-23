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
[EnableRateLimiting("AiRubikScannerTest")]
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

        return Ok(await aiRubikClient.StartScannerTestSessionAsync(cancellationToken));
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId, [FromServices] IAiRubikClient aiRubikClient, CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        return Ok(await aiRubikClient.GetScannerTestSessionAsync(sessionId, cancellationToken));
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

        var imageBase64 = await ReadAsBase64Async(request.Snapshot, cancellationToken);
        return Ok(await aiRubikClient.PreviewScannerTestFrameAsync(sessionId, imageBase64, BuildScannerMetadata(request), cancellationToken));
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

        var imageBase64 = await ReadAsBase64Async(request.Snapshot, cancellationToken);
        return Ok(await aiRubikClient.ObserveScannerTestFrameAsync(sessionId, imageBase64, BuildScannerMetadata(request), cancellationToken));
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

        var framesBase64 = new List<string>(request.Frames.Count);
        foreach (var frame in request.Frames)
        {
            framesBase64.Add(await ReadAsBase64Async(frame, cancellationToken));
        }

        return Ok(await aiRubikClient.ScanScannerTestFaceAsync(sessionId, framesBase64, cancellationToken));
    }

    [HttpPost("sessions/{sessionId}/retry-face")]
    public async Task<IActionResult> RetryFace(string sessionId, [FromServices] IAiRubikClient aiRubikClient, CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        return Ok(await aiRubikClient.RetryScannerTestFaceAsync(sessionId, cancellationToken));
    }

    [HttpPost("sessions/{sessionId}/reset")]
    public async Task<IActionResult> ResetSession(string sessionId, [FromServices] IAiRubikClient aiRubikClient, CancellationToken cancellationToken)
    {
        var gate = EnsureEnabled();
        if (gate is not null)
            return gate;

        return Ok(await aiRubikClient.ResetScannerTestSessionAsync(sessionId, cancellationToken));
    }

    private IActionResult? EnsureEnabled()
    {
        if (!_environment.IsDevelopment())
            return NotFound();
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
