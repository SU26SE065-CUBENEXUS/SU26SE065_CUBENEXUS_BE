using System.Security.Claims;
using CubeNexus.Application.DTOs.Tournament;
using CubeNexus.Application.DTOs.Registration;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
public class TournamentManagementController : ControllerBase
{
    private readonly ITournamentService _tournamentService;
    private readonly ITournamentRegistrationService _registrationService;
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteTournamentUseCase _completeTournamentUseCase;

    public TournamentManagementController(
        ITournamentService tournamentService,
        ITournamentRegistrationService registrationService,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteTournamentUseCase completeTournamentUseCase)
    {
        _tournamentService = tournamentService;
        _registrationService = registrationService;
        _completeTournamentUseCase = completeTournamentUseCase;
    }

    /// <summary>
    /// Tạo giải đấu mới kèm theo danh sách các Events và MedleyPuzzles (nếu có).
    /// Chỉ dành cho ADMIN hoặc MANAGER.
    /// </summary>
    [HttpPost("api/tournament-management/tournaments")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> CreateTournament([FromBody] CreateTournamentDto dto)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out var managerId))
            {
                return Unauthorized(new { message = "Invalid user token." });
            }

            var result = await _tournamentService.CreateTournamentAsync(dto, managerId);
            return CreatedAtAction(
                actionName: "GetById",
                controllerName: "Tournament",
                routeValues: new { id = result.Id },
                value: result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while creating the tournament.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Manager/Admin override seed time thủ công cho một event đăng ký.
    /// </summary>
    [HttpPatch("api/tournament-management/registrations/{registrationEventId:guid}/override-seed")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> OverrideSeed(Guid registrationEventId, [FromBody] OverrideSeedDto dto)
    {
        try
        {
            var result = await _registrationService.OverrideSeedAsync(registrationEventId, dto);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while overriding seed.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Lấy danh sách competitors đăng ký cho một event, được sort theo seed_time_ms ASC NULLS LAST.
    /// </summary>
    [HttpGet("api/tournament-management/events/{eventId:guid}/competitors")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> GetEventCompetitors(Guid eventId)
    {
        try
        {
            var result = await _registrationService.GetEventCompetitorsSortedAsync(eventId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching event competitors.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Complete giải đấu (MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-management/tournaments/{tournamentId:guid}/complete")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> CompleteTournament(Guid tournamentId)
    {
        try
        {
            var result = await _completeTournamentUseCase.ExecuteAsync(tournamentId);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while completing the tournament.", detail = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/tournament-management/tournaments/{tournamentId}/registrations
    /// Lấy danh sách toàn bộ đăng ký của giải đấu (ADMIN, MANAGER).
    /// </summary>
    [HttpGet("api/tournament-management/tournaments/{tournamentId:guid}/registrations")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> GetTournamentRegistrations(Guid tournamentId, System.Threading.CancellationToken ct)
    {
        try
        {
            var result = await _registrationService.GetTournamentRegistrationsAsync(tournamentId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching registrations.", detail = ex.Message });
        }
    }

    /// <summary>
    /// PATCH /api/tournament-management/registrations/{registrationId}/status
    /// Phê duyệt hoặc hủy đăng ký của competitor (ADMIN, MANAGER).
    /// </summary>
    [HttpPatch("api/tournament-management/registrations/{registrationId:guid}/status")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> UpdateRegistrationStatus(Guid registrationId, [FromBody] UpdateRegistrationStatusDto dto, System.Threading.CancellationToken ct)
    {
        try
        {
            var result = await _registrationService.UpdateRegistrationStatusAsync(registrationId, dto.Status, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while updating status.", detail = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/tournament-management/registrations/{registrationId}/check-in
    /// Điểm danh thủ công tại quầy cho competitor (ADMIN, MANAGER).
    /// </summary>
    [HttpPost("api/tournament-management/registrations/{registrationId:guid}/check-in")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> ManuallyCheckIn(Guid registrationId, System.Threading.CancellationToken ct)
    {
        try
        {
            var result = await _registrationService.ManuallyCheckInAsync(registrationId, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while performing check-in.", detail = ex.Message });
        }
    }

    private IActionResult HandleCustomException(CubeNexus.Application.Exceptions.CustomException ex)
    {
        var response = new Dictionary<string, object>
        {
            { "code", ex.ErrorCode },
            { "message", ex.Message }
        };
        if (ex.ExtraData is Dictionary<string, object> dict)
        {
            foreach (var kvp in dict)
            {
                response[kvp.Key] = kvp.Value;
            }
        }
        return StatusCode(ex.StatusCode, response);
    }
}
