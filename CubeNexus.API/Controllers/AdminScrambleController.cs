using System.Security.Claims;
using CubeNexus.Application.DTOs.Admin;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/admin/scrambles")]
[Authorize(Roles = "ADMIN")]
public sealed class AdminScrambleController : ControllerBase
{
    private readonly IAdminScrambleService _service;
    public AdminScrambleController(IAdminScrambleService service) => _service = service;

    [HttpGet("summary")]
    public Task<IActionResult> Summary(CancellationToken ct) =>
        ExecuteAsync(() => _service.GetSummaryAsync(ct));

    [HttpGet]
    public Task<IActionResult> Items([FromQuery] string? mode, [FromQuery] string? status,
        [FromQuery] Guid? puzzleTypeId, [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        CancellationToken ct = default) => ExecuteAsync(() => _service.GetItemsAsync(mode, status, puzzleTypeId, page, pageSize, ct));

    [HttpPost("generate")]
    public Task<IActionResult> Generate([FromBody] GenerateScramblesRequestDto request, CancellationToken ct) =>
        ExecuteAsync(() => _service.GenerateAsync(request, GetUserId(), ct));

    [HttpPost("import")]
    public Task<IActionResult> Import([FromBody] ImportScramblesRequestDto request, CancellationToken ct) =>
        ExecuteAsync(() => _service.ImportAsync(request, GetUserId(), ct));

    [HttpPost("{id:guid}/approve")]
    public Task<IActionResult> Approve(Guid id, CancellationToken ct) =>
        ExecuteAsync(() => _service.ApproveAsync(id, GetUserId(), ct));

    [HttpPost("{id:guid}/retire")]
    public Task<IActionResult> Retire(Guid id, CancellationToken ct) =>
        ExecuteAsync(() => _service.RetireAsync(id, GetUserId(), ct));

    [HttpGet("mode")]
    public async Task<IActionResult> GetMode(CancellationToken ct)
    {
        var mode = await _service.GetScrambleGenerationModeAsync(ct);
        return Ok(new { mode });
    }

    [HttpPost("mode")]
    public async Task<IActionResult> SetMode([FromBody] SetScrambleModeRequest request, CancellationToken ct)
    {
        var mode = await _service.SetScrambleGenerationModeAsync(request.Mode, GetUserId(), ct);
        return Ok(new { mode });
    }

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = "NOT_FOUND", message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "FORBIDDEN", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = "INVALID_SCRAMBLE", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = "INVALID_OPERATION", message = ex.Message });
        }
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue("id") ?? User.FindFirstValue("userId") ??
                  User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : throw new UnauthorizedAccessException("Token không chứa user id hợp lệ.");
    }
}

public class SetScrambleModeRequest
{
    public string Mode { get; set; } = "MANUAL";
}
