using System.Security.Claims;
using CubeNexus.Application.DTOs.Practice;
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("solves")]
    public async Task<IActionResult> SubmitSolve([FromBody] SubmitSolveDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.SubmitSolveAsync(userId, dto);
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Token không chứa userId hợp lệ.");

        return userId;
    }
}