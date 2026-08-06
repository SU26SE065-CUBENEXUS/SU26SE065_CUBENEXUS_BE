using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.UseCases.OnlineArena;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/admin/elo")]
[Authorize(Roles = "ADMIN,MANAGER")]
public class OnlineArenaAdminEloController : ControllerBase
{
    private bool TryGetCurrentUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var rawUserId = User.FindFirstValue("id")
            ?? User.FindFirstValue("userId")
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return !string.IsNullOrWhiteSpace(rawUserId) && Guid.TryParse(rawUserId, out userId);
    }

    private IActionResult Unauthorized401()
        => Unauthorized(new { code = "UNAUTHORIZED", message = "Missing or invalid user id claim in token." });

    private IActionResult MapException(Exception ex) => ex switch
    {
        ConflictException conflict => Conflict(new { code = "CONFLICT", message = conflict.Message }),
        UnauthorizedAccessException forbidden => StatusCode(StatusCodes.Status403Forbidden, new { code = "FORBIDDEN", message = forbidden.Message }),
        KeyNotFoundException notFound => NotFound(new { code = "NOT_FOUND", message = notFound.Message }),
        InvalidOperationException invalidOperation => BadRequest(new { code = "BAD_REQUEST", message = invalidOperation.Message }),
        ArgumentException argument => BadRequest(new { code = "BAD_REQUEST", message = argument.Message }),
        _ => StatusCode(StatusCodes.Status500InternalServerError, new { code = "SERVER_ERROR", message = ex.Message })
    };

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig([FromServices] GetEloConfigUseCase useCase, CancellationToken ct)
    {
        try
        {
            return Ok(await useCase.ExecuteAsync(ct));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPut("config")]
    public async Task<IActionResult> UpdateConfig(
        [FromBody] UpdateEloConfigRequest request,
        [FromServices] UpdateEloConfigUseCase useCase,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var adminId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(adminId, request, ct));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("players")]
    public async Task<IActionResult> GetPlayerEloList(
        [FromQuery] Guid? puzzleTypeId,
        [FromQuery] string? search,
        [FromServices] GetAdminPlayerEloListUseCase useCase)
    {
        try
        {
            return Ok(await useCase.ExecuteAsync(puzzleTypeId, search));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("players/{userId:guid}/adjust")]
    public async Task<IActionResult> AdjustPlayerElo(
        Guid userId,
        [FromBody] AdjustPlayerEloRequest request,
        [FromServices] AdjustPlayerEloUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var adminId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(adminId, userId, request));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }
}
