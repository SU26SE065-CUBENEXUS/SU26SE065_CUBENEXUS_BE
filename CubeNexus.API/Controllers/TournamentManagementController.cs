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
            return StatusCode(201, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CreateTournament 500 ERROR] {ex}");
            return StatusCode(500, new { 
                message = "An error occurred while creating the tournament.", 
                detail = ex.Message,
                inner = ex.InnerException?.Message 
            });
        }
    }

    /// <summary>
    /// Lấy các tournament do Manager hiện tại tạo hoặc được phân công.
    /// Không dùng endpoint public vì endpoint public trả về tất cả tournament đã publish.
    /// </summary>
    [HttpGet("api/tournament-management/tournaments/my")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> GetMyTournaments(CancellationToken ct)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var managerId))
            return Unauthorized(new { message = "Invalid user token." });

        var result = await _tournamentService.GetManagerTournamentsAsync(managerId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Đóng cổng đăng ký giải đấu thủ công tức thì (MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-management/tournaments/{tournamentId:guid}/close-registration")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> CloseRegistration(Guid tournamentId)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userIdString, out var managerId);

            var result = await _tournamentService.CloseRegistrationAsync(tournamentId, managerId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while closing registration.", detail = ex.Message });
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
            await _completeTournamentUseCase.ExecuteAsync(tournamentId);
            var updatedTournament = await _tournamentService.GetTournamentByIdAsync(tournamentId);
            return Ok(updatedTournament);
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

    /// <summary>
    /// Lấy danh sách Trọng tài thuộc Giải đấu.
    /// </summary>
    [HttpGet("api/tournament-management/tournaments/{tournamentId:guid}/judges")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> GetTournamentJudges(Guid tournamentId, CancellationToken ct)
    {
        try
        {
            var result = await _tournamentService.GetTournamentJudgesAsync(tournamentId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching judges.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Tạo 1 tài khoản Trọng tài đơn lẻ cho Giải đấu.
    /// </summary>
    [HttpPost("api/tournament-management/tournaments/{tournamentId:guid}/judges")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> CreateTournamentJudge(Guid tournamentId, [FromBody] CreateTournamentJudgeDto dto, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userIdString, out var managerId);

            var result = await _tournamentService.CreateTournamentJudgeAsync(tournamentId, dto, managerId, ct);
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
            return StatusCode(500, new { message = "An error occurred while creating judge.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Tạo HÀNG LOẠT tài khoản Trọng tài trong 1-click cho Giải đấu.
    /// </summary>
    [HttpPost("api/tournament-management/tournaments/{tournamentId:guid}/judges/batch")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> BatchCreateTournamentJudges(Guid tournamentId, [FromBody] BatchCreateTournamentJudgeDto dto, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userIdString, out var managerId);

            var result = await _tournamentService.BatchCreateTournamentJudgesAsync(tournamentId, dto, managerId, ct);
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
            return StatusCode(500, new { message = "An error occurred while batch creating judges.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Đổi vị trí / Tráo ngẫu nhiên vai trò & bàn thi của Trọng tài trong Giải đấu.
    /// </summary>
    [HttpPost("api/tournament-management/tournaments/{tournamentId:guid}/judges/shuffle")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> ShuffleTournamentJudges(Guid tournamentId, [FromBody] ShuffleTournamentJudgesDto dto, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userIdString, out var managerId);

            var result = await _tournamentService.ShuffleTournamentJudgesAsync(tournamentId, dto, managerId, ct);
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
            return StatusCode(500, new { message = "An error occurred while shuffling judges.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật thông tin (DisplayName, RoleCode, AssignedStationNumber) của Trọng tài.
    /// </summary>
    [HttpPut("api/tournament-management/tournaments/{tournamentId:guid}/judges/{judgeUserId:guid}")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> UpdateTournamentJudge(Guid tournamentId, Guid judgeUserId, [FromBody] UpdateTournamentJudgeDto dto, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userIdString, out var managerId);

            var result = await _tournamentService.UpdateTournamentJudgeAsync(tournamentId, judgeUserId, dto, managerId, ct);
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
            return StatusCode(500, new { message = "An error occurred while updating judge.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Bật/Tắt trạng thái hoạt động (IsActive) của Trọng tài.
    /// </summary>
    [HttpPatch("api/tournament-management/tournaments/{tournamentId:guid}/judges/{judgeUserId:guid}/toggle-status")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> ToggleJudgeStatus(Guid tournamentId, Guid judgeUserId, [FromBody] ToggleJudgeStatusDto dto, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userIdString, out var managerId);

            var result = await _tournamentService.ToggleJudgeStatusAsync(tournamentId, judgeUserId, dto.IsActive, managerId, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while toggling judge status.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Vô hiệu hóa hàng loạt toàn bộ Trọng tài của Giải đấu.
    /// </summary>
    [HttpPost("api/tournament-management/tournaments/{tournamentId:guid}/judges/deactivate-all")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> DeactivateAllJudges(Guid tournamentId, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userIdString, out var managerId);

            var result = await _tournamentService.DeactivateAllJudgesAsync(tournamentId, managerId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while deactivating all judges.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Kích hoạt hàng loạt toàn bộ Trọng tài của Giải đấu.
    /// </summary>
    [HttpPost("api/tournament-management/tournaments/{tournamentId:guid}/judges/activate-all")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> ActivateAllJudges(Guid tournamentId, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userIdString, out var managerId);

            var result = await _tournamentService.ActivateAllJudgesAsync(tournamentId, managerId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while activating all judges.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Đặt lại mật khẩu mới cho Trọng tài.
    /// </summary>
    [HttpPost("api/tournament-management/tournaments/{tournamentId:guid}/judges/{judgeUserId:guid}/reset-password")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> ResetTournamentJudgePassword(Guid tournamentId, Guid judgeUserId, [FromBody] ResetJudgePasswordDto dto, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userIdString, out var managerId);

            var result = await _tournamentService.ResetTournamentJudgePasswordAsync(tournamentId, judgeUserId, dto, managerId, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while resetting judge password.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Xóa/gỡ Trọng tài khỏi Giải đấu.
    /// </summary>
    [HttpDelete("api/tournament-management/tournaments/{tournamentId:guid}/judges/{judgeUserId:guid}")]
    [Authorize(Roles = "ADMIN,MANAGER")]
    public async Task<IActionResult> DeleteTournamentJudge(Guid tournamentId, Guid judgeUserId, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(userIdString, out var managerId);

            await _tournamentService.DeleteTournamentJudgeAsync(tournamentId, judgeUserId, managerId, ct);
            return Ok(new { message = "Judge removed successfully." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while deleting judge.", detail = ex.Message });
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
