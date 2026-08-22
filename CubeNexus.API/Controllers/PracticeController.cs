using System.Security.Claims;
using CubeNexus.Application.DTOs.Practice;
using CubeNexus.Application.Exceptions;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/practice")]
[Authorize(Roles = "COMPETITOR")]
public class PracticeController : ControllerBase
{
    private readonly IPracticeService _practiceService;

    public PracticeController(IPracticeService practiceService)
    {
        _practiceService = practiceService;
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession([FromBody] StartPracticeSessionDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.StartSessionAsync(userId, dto);

            return CreatedAtAction(
                nameof(GetSessionDetail),
                new { sessionId = result.Id },
                result
            );
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.ErrorCode, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("solves")]
    [Obsolete("Use WCA attempt flow.")]
    public async Task<IActionResult> SubmitSolve([FromBody] SubmitSolveDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.SubmitSolveAsync(userId, dto);
            return Ok(result);
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.ErrorCode, message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("sessions/{sessionId:guid}/connect")]
    public async Task<IActionResult> ConnectSession(Guid sessionId)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _practiceService.ConnectSessionAsync(userId, sessionId);
            return Ok(new { message = "Practice mobile timer connected." });
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.ErrorCode, message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("sessions/{sessionId:guid}/disconnect")]
    public async Task<IActionResult> DisconnectSession(Guid sessionId)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _practiceService.DisconnectSessionAsync(userId, sessionId);
            return Ok(new { message = "Practice mobile timer disconnected." });
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.ErrorCode, message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("sessions/{sessionId:guid}/attempts")]
    public async Task<IActionResult> CreateAttempt(Guid sessionId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.CreateAttemptAsync(userId, sessionId);
            return CreatedAtAction(nameof(GetAttempt), new { attemptId = result.Id }, result);
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.ErrorCode, message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("sessions/{sessionId:guid}/current-attempt")]
    public async Task<IActionResult> GetCurrentAttempt(Guid sessionId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.GetCurrentAttemptAsync(userId, sessionId);
            return result == null ? NoContent() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("attempts/{attemptId:guid}")]
    public async Task<IActionResult> GetAttempt(Guid attemptId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.GetAttemptAsync(userId, attemptId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("attempts/{attemptId:guid}/hands-on")]
    public async Task<IActionResult> HandsOn(Guid attemptId)
    {
        return await AttemptAction(userId => _practiceService.HandsOnAsync(userId, attemptId));
    }

    [HttpPost("attempts/{attemptId:guid}/ready")]
    public async Task<IActionResult> Ready(Guid attemptId)
    {
        return await AttemptAction(userId => _practiceService.ReadyAsync(userId, attemptId));
    }

    [HttpPost("attempts/{attemptId:guid}/hands-off")]
    public async Task<IActionResult> HandsOff(Guid attemptId)
    {
        return await AttemptAction(userId => _practiceService.HandsOffAsync(userId, attemptId));
    }

    [HttpPost("attempts/{attemptId:guid}/finalize")]
    public async Task<IActionResult> FinalizeAttempt(
        Guid attemptId, [FromBody] FinalizeAttemptDto dto)
    {
        return await AttemptAction(userId =>
            _practiceService.FinalizeAttemptAsync(userId, attemptId, dto));
    }

    [HttpPost("attempts/{attemptId:guid}/abort")]
    public async Task<IActionResult> AbortAttempt(
        Guid attemptId, [FromBody] AbortAttemptDto? dto)
    {
        return await AttemptAction(userId =>
            _practiceService.AbortAttemptAsync(userId, attemptId, dto));
    }

    [HttpPost("sessions/{sessionId:guid}/end")]
    public async Task<IActionResult> EndSession(Guid sessionId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.EndSessionAsync(userId, sessionId);
            return Ok(result);
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.ErrorCode, message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetMySessions(
        [FromQuery] Guid? puzzleTypeId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetCurrentUserId();
        var result = await _practiceService.GetMySessionsAsync(userId, puzzleTypeId, page, pageSize);
        return Ok(result);
    }

    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> GetSessionDetail(Guid sessionId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.GetSessionDetailAsync(userId, sessionId);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private async Task<IActionResult> AttemptAction(
        Func<Guid, Task<PracticeAttemptResponseDto>> action)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await action(userId);
            return Ok(result);
        }
        catch (CustomException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.ErrorCode, message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Token không chứa userId hợp lệ.");

        return userId;
    }
}
