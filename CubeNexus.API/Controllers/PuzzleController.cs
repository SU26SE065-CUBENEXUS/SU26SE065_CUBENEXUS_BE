using CubeNexus.Application.DTOs.Puzzle;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

/// <summary>
/// Quản lý loại Rubik (Puzzle Type).
/// - GET (danh sách/chi tiết): Public, không cần đăng nhập.
/// - POST / PUT / PATCH / DELETE: Chỉ ADMIN.
/// </summary>
[ApiController]
[Route("api/puzzles")]
public class PuzzleController : ControllerBase
{
    private readonly IPuzzleService _puzzleService;

    public PuzzleController(IPuzzleService puzzleService)
    {
        _puzzleService = puzzleService;
    }

    // ── GET: danh sách ───────────────────────────────────────────────────────

    /// <summary>
    /// Lấy danh sách loại Rubik.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var result = await _puzzleService.GetAllAsync();
        return Ok(result);
    }

    [HttpGet("admin")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> GetAllForAdmin([FromQuery] bool includeInactive = true)
    {
        var result = await _puzzleService.GetAllAsync();
        return Ok(result);
    }



    /// <summary>Lấy thông tin chi tiết 1 loại Rubik theo ID</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var result = await _puzzleService.GetByIdAsync(id);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // ── POST: tạo mới ────────────────────────────────────────────────────────

    /// <summary>Admin: Tạo loại Rubik mới</summary>
    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Create([FromBody] CreatePuzzleTypeDto dto)
    {
        try
        {
            var result = await _puzzleService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // ── PUT: cập nhật ────────────────────────────────────────────────────────

    /// <summary>Admin: Cập nhật thông tin loại Rubik</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePuzzleTypeDto dto)
    {
        try
        {
            var result = await _puzzleService.UpdateAsync(id, dto);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // ── PATCH: thay đổi trạng thái ───────────────────────────────────────────

    /// <summary>Admin: Vô hiệu hoá (xóa mềm) loại Rubik</summary>
    [HttpPatch("{id:guid}/deactivate")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        try
        {
            await _puzzleService.DeactivateAsync(id);
            return Ok(new { message = "Đã vô hiệu hoá loại Rubik thành công." });
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

    /// <summary>Admin: Kích hoạt lại loại Rubik đã bị vô hiệu hoá</summary>
    [HttpPatch("{id:guid}/activate")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Activate(Guid id)
    {
        try
        {
            await _puzzleService.ActivateAsync(id);
            return Ok(new { message = "Đã kích hoạt lại loại Rubik thành công." });
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
}
