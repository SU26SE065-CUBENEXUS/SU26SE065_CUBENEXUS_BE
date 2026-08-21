using System.Security.Claims;
using CubeNexus.Application.DTOs.FaceVerification;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Infrastructure.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CubeNexus.API.Controllers;

[ApiController]
public class FaceVerificationController : ControllerBase
{
    private readonly IFaceVerificationService _service;
    private readonly IFaceVerificationClient _client;
    private readonly FaceVerificationOptions _options;

    public FaceVerificationController(
        IFaceVerificationService service,
        IFaceVerificationClient client,
        IOptions<FaceVerificationOptions> options)
    {
        _service = service;
        _client = client;
        _options = options.Value;
    }

    [HttpGet("api/face-verification/health")]
    [Authorize]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var health = await _client.GetHealthAsync(ct);
        return Ok(health ?? new { status = "unavailable" });
    }

    [HttpGet("api/face-verification/enrollment/me")]
    [Authorize]
    public async Task<IActionResult> GetMyEnrollment(CancellationToken ct)
    {
        var userId = RequireUserId();
        return Ok(await _service.GetEnrollmentStatusAsync(userId, ct));
    }

    [HttpPost("api/face-verification/enrollment/sessions")]
    [Authorize(Roles = "COMPETITOR")]
    public async Task<IActionResult> StartEnrollment(CancellationToken ct)
    {
        try
        {
            var userId = RequireUserId();
            return Ok(await _service.StartEnrollmentAsync(userId, ct));
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpPost("api/face-verification/enrollment/sessions/{sessionId:guid}/evidence")]
    [Authorize(Roles = "COMPETITOR")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> SubmitEnrollmentEvidence(
        Guid sessionId,
        [FromForm] EnrollmentEvidenceFormRequest form,
        CancellationToken ct)
    {
        try
        {
            var userId = RequireUserId();
            var result = await _service.SubmitEnrollmentEvidenceAsync(
                sessionId,
                userId,
                form.EvidenceVideo is null ? null : ToUpload(form.EvidenceVideo),
                (form.Images ?? []).Select(ToUpload).ToList(),
                form.Metadata ?? "{}",
                ct);
            return Ok(result);
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    /// <summary>Competitor: thử xác minh Face ID đã enroll (không phải check-in).</summary>
    [HttpPost("api/face-verification/self-test/sessions")]
    [Authorize]
    public async Task<IActionResult> StartSelfTestSession(CancellationToken ct)
    {
        try
        {
            var userId = RequireUserId();
            return Ok(await _service.StartSelfTestVerificationAsync(userId, ct));
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    /// <summary>Competitor: validate tournament/registration, then start Face Verification before showing QR.</summary>
    [HttpPost("api/face-verification/competitor/check-in/sessions")]
    [Authorize(Roles = "COMPETITOR")]
    public async Task<IActionResult> StartCompetitorCheckInSession(
        [FromBody] StartCompetitorCheckInFaceRequestDto dto,
        CancellationToken ct)
    {
        try
        {
            var userId = RequireUserId();
            return Ok(await _service.StartCompetitorCheckInVerificationAsync(userId, dto.TournamentId, ct));
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    /// <summary>Judge desk: QR → create face verification session for that competitor.</summary>
    [HttpPost("api/face-verification/check-in/sessions")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN")]
    public async Task<IActionResult> StartCheckInSession([FromBody] StartCheckInFaceRequestDto dto, CancellationToken ct)
    {
        try
        {
            var judgeId = RequireUserId();
            return Ok(await _service.StartCheckInVerificationAsync(dto.QrToken, judgeId, ct));
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpPost("api/face-verification/sessions/{sessionId:guid}/passive-evidence")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN,COMPETITOR")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> SubmitPassive(
        Guid sessionId,
        [FromForm] PassiveEvidenceFormRequest form,
        CancellationToken ct)
    {
        try
        {
            var userId = RequireUserId();
            return Ok(await _service.SubmitPassiveEvidenceAsync(
                sessionId,
                userId,
                (form.FinalFrames ?? []).Select(ToUpload).ToList(),
                ct));
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpPost("api/face-verification/sessions/{sessionId:guid}/evidence")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN,COMPETITOR")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> SubmitActive(
        Guid sessionId,
        [FromForm] ActiveEvidenceFormRequest form,
        CancellationToken ct)
    {
        try
        {
            var userId = RequireUserId();
            return Ok(await _service.SubmitActiveEvidenceAsync(
                sessionId,
                userId,
                form.EvidenceVideo is null ? null : ToUpload(form.EvidenceVideo),
                (form.FinalFrames ?? []).Select(ToUpload).ToList(),
                form.Metadata ?? "{\"cameraMirror\":true}",
                ct));
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpGet("api/face-verification/sessions/{sessionId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetSession(Guid sessionId, CancellationToken ct)
    {
        try
        {
            var userId = RequireUserId();
            var isStaff = User.IsInRole("JUDGE") || User.IsInRole("MANAGER") || User.IsInRole("ADMIN");
            return Ok(await _service.GetSessionAsync(sessionId, userId, isStaff, ct));
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
    }

    [HttpPost("api/face-verification/analyze-frame")]
    [Authorize]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> AnalyzeFrame([FromForm] AnalyzeFrameFormRequest form, CancellationToken ct)
    {
        if (form.Frame is null)
        {
            return BadRequest(new { errorCode = "FRAME_REQUIRED", message = "frame is required." });
        }

        return Ok(await _service.AnalyzeFrameAsync(ToUpload(form.Frame), ct));
    }

    /// <summary>FastAPI callback — business source of truth update.</summary>
    [HttpPost("internal/face-verification/result")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromBody] FaceCallbackRequestDto dto, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_options.CallbackApiKey))
        {
            if (!Request.Headers.TryGetValue("X-Face-Callback-Key", out var key)
                || key != _options.CallbackApiKey)
            {
                return Unauthorized();
            }
        }

        await _service.HandleCallbackAsync(dto, ct);
        return Ok(new { received = true });
    }

    private static FaceUploadFile ToUpload(IFormFile file)
        => new()
        {
            Content = file.OpenReadStream(),
            FileName = string.IsNullOrWhiteSpace(file.FileName) ? "upload.bin" : file.FileName,
            ContentType = file.ContentType,
        };

    private Guid RequireUserId()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId))
        {
            throw new CustomException("UNAUTHORIZED", "Invalid user token.", 401);
        }

        return userId;
    }

    public class EnrollmentEvidenceFormRequest
    {
        public IFormFile? EvidenceVideo { get; set; }
        public List<IFormFile>? Images { get; set; }
        public string? Metadata { get; set; }
    }

    public class PassiveEvidenceFormRequest
    {
        public List<IFormFile>? FinalFrames { get; set; }
    }

    public class ActiveEvidenceFormRequest
    {
        public IFormFile? EvidenceVideo { get; set; }
        public List<IFormFile>? FinalFrames { get; set; }
        public string? Metadata { get; set; }
    }

    public class AnalyzeFrameFormRequest
    {
        public IFormFile Frame { get; set; } = null!;
    }
}
