using System.Security.Claims;
using CubeNexus.Application.DTOs.Practice;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

/// <summary>
/// Luồng tập luyện dành cho Competitor.
/// Tất cả các endpoint đều yêu cầu đăng nhập với role COMPETITOR.
/// </summary>
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

    // ── Bắt đầu session ──────────────────────────────────────────────────────

    /// <summary>
    /// Bắt đầu một session tập luyện mới với loại Rubik được chọn.
    /// Trả về ID session để dùng cho các lần ghi solve tiếp theo.
    /// </summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession([FromBody] StartPracticeSessionDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.StartSessionAsync(userId, dto);
            return CreatedAtAction(nameof(GetSessionDetail), new { sessionId = result.Id }, result);
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

    // ── Submit solve ─────────────────────────────────────────────────────────

    /// <summary>
    /// Ghi nhận 1 lần giải Rubik trong session đang tập.
    /// Trả về thời gian + Ao5 rolling (WCA: loại best/worst trong 5 solve gần nhất).
    /// Penalty chấp nhận: ok / OK / PLUS_2 / plus_2 / DNF / dnf (không phân biệt hoa/thường).
    /// </summary>
    [HttpPost("solves")]
    public async Task<IActionResult> SubmitSolve([FromBody] SubmitSolveDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.SubmitSolveAsync(userId, dto);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
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

    // ── Kết thúc session ─────────────────────────────────────────────────────

    /// <summary>
    /// Kết thúc session và trả về bảng tổng kết:
    /// số solve, DNF, trung bình, best, Ao5 tốt nhất.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/end")]
    public async Task<IActionResult> EndSession(Guid sessionId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.EndSessionAsync(userId, sessionId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    // ── Lịch sử ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Lấy danh sách session tập luyện của bản thân (có phân trang).
    /// Có thể lọc theo puzzleTypeId.
    /// </summary>
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

    /// <summary>
    /// Xem chi tiết một session: tất cả lần giải + stats tổng.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> GetSessionDetail(Guid sessionId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _practiceService.GetSessionDetailAsync(userId, sessionId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub")
               ?? throw new UnauthorizedAccessException("Không tìm thấy thông tin người dùng trong token.");

        return Guid.Parse(sub);
    }
}
