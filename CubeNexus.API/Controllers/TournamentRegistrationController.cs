using System.Security.Claims;
using CubeNexus.Application.DTOs.Registration;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
public class TournamentRegistrationController : ControllerBase
{
    private readonly ITournamentRegistrationService _registrationService;

    public TournamentRegistrationController(ITournamentRegistrationService registrationService)
    {
        _registrationService = registrationService;
    }

    /// <summary>
    /// Competitor đăng ký tham gia một giải đấu (chỉ vai trò COMPETITOR).
    /// </summary>
    [HttpPost("api/tournament-registration/tournaments/{tournamentId:guid}/register")]
    [Authorize(Roles = "COMPETITOR")]
    public async Task<IActionResult> Register(Guid tournamentId, [FromBody] RegisterTournamentDto dto)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var result = await _registrationService.RegisterCompetitorAsync(tournamentId, userId, dto);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred during registration.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Lấy danh sách các giải đấu mà người dùng hiện tại đã đăng ký.
    /// </summary>
    [HttpGet("api/me/registrations")]
    [Authorize]
    public async Task<IActionResult> GetMyRegistrations()
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var result = await _registrationService.GetUserRegistrationsAsync(userId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching registrations.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Lấy chi tiết một đăng ký của người dùng hiện tại, bao gồm cả QR Token payload.
    /// </summary>
    [HttpGet("api/me/registrations/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetMyRegistrationById(Guid id)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var result = await _registrationService.GetUserRegistrationByIdAsync(id, userId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching the registration.", detail = ex.Message });
        }
    }
}
