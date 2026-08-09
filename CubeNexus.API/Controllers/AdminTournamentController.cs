using CubeNexus.Application.DTOs.Admin;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/admin/tournaments")]
[Authorize(Roles = "ADMIN")]
public class AdminTournamentController : ControllerBase
{
    private readonly IAdminTournamentService _adminTournamentService;

    public AdminTournamentController(IAdminTournamentService adminTournamentService)
    {
        _adminTournamentService = adminTournamentService;
    }

    private IActionResult MapException(Exception ex)
    {
        var msg = ex.InnerException != null ? $"{ex.Message}: {ex.InnerException.Message}" : ex.Message;
        return ex switch
        {
            UnauthorizedAccessException forbidden => StatusCode(StatusCodes.Status403Forbidden, new { code = "FORBIDDEN", message = forbidden.Message }),
            KeyNotFoundException notFound => NotFound(new { code = "NOT_FOUND", message = notFound.Message }),
            InvalidOperationException invalidOperation => BadRequest(new { code = "BAD_REQUEST", message = invalidOperation.Message }),
            ArgumentException argument => BadRequest(new { code = "BAD_REQUEST", message = argument.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { code = "SERVER_ERROR", message = msg })
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetTournaments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _adminTournamentService.GetTournamentsAsync(page, pageSize, search, status, ct));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTournamentById(
        Guid id,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _adminTournamentService.GetTournamentByIdAsync(id, ct));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateTournamentStatus(
        Guid id,
        [FromBody] UpdateTournamentStatusRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _adminTournamentService.UpdateTournamentStatusAsync(id, request.StatusCode, ct));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }
}
