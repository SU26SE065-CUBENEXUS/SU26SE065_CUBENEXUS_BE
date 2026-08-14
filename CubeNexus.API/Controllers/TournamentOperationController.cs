using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CubeNexus.API.Hubs;

namespace CubeNexus.API.Controllers;

[ApiController]
public class TournamentOperationController : ControllerBase
{
    private readonly ITournamentOperationService _operationService;
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICheckInRegistrationByQrUseCase _checkInUseCase;
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IStartRoundUseCase _startRoundUseCase;
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ILockRoundResultsUseCase _lockRoundResultsUseCase;
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteRoundUseCase _completeRoundUseCase;
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteEventUseCase _completeEventUseCase;
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IAdvanceRoundUseCase _advanceRoundUseCase;
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IVerifyJudgeStationByStationUseCase _verifyJudgeStationByStationUseCase;
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICorrectResultUseCase _correctResultUseCase;
    private readonly CubeNexus.Application.Interfaces.Repositories.IUnitOfWork _unitOfWork;
    private readonly IHubContext<TournamentHub> _hubContext;

    public TournamentOperationController(
        ITournamentOperationService operationService,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICheckInRegistrationByQrUseCase checkInUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IStartRoundUseCase startRoundUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ILockRoundResultsUseCase lockRoundResultsUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteRoundUseCase completeRoundUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteEventUseCase completeEventUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IAdvanceRoundUseCase advanceRoundUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IVerifyJudgeStationByStationUseCase verifyJudgeStationByStationUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICorrectResultUseCase correctResultUseCase,
        CubeNexus.Application.Interfaces.Repositories.IUnitOfWork unitOfWork,
        IHubContext<TournamentHub> hubContext)
    {
        _operationService = operationService;
        _checkInUseCase = checkInUseCase;
        _startRoundUseCase = startRoundUseCase;
        _lockRoundResultsUseCase = lockRoundResultsUseCase;
        _completeRoundUseCase = completeRoundUseCase;
        _completeEventUseCase = completeEventUseCase;
        _advanceRoundUseCase = advanceRoundUseCase;
        _verifyJudgeStationByStationUseCase = verifyJudgeStationByStationUseCase;
        _correctResultUseCase = correctResultUseCase;
        _unitOfWork = unitOfWork;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Complete event (MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-operation/events/{eventId:guid}/complete")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> CompleteEvent(Guid eventId, CancellationToken ct)
    {
        try
        {
            var result = await _completeEventUseCase.ExecuteAsync(eventId);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while completing the event.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Lock tất cả kết quả của một vòng thi (MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-operation/events/{eventId:guid}/rounds/{roundNumber:int}/lock-results")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> LockRoundResults(Guid eventId, int roundNumber, CancellationToken ct)
    {
        try
        {
            var result = await _lockRoundResultsUseCase.ExecuteAsync(eventId, roundNumber);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while locking results.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Complete một vòng thi (MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-operation/events/{eventId:guid}/rounds/{roundNumber:int}/complete")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> CompleteRound(Guid eventId, int roundNumber, CancellationToken ct)
    {
        try
        {
            var result = await _completeRoundUseCase.ExecuteAsync(eventId, roundNumber);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while completing the round.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Bắt đầu một vòng thi của Event (MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-operation/events/{eventId:guid}/rounds/{roundNumber:int}/start")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> StartRound(Guid eventId, int roundNumber, [FromBody] CubeNexus.Application.DTOs.Operation.StartRoundRequestDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _startRoundUseCase.ExecuteAsync(eventId, roundNumber, dto);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while starting the round.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Check-in competitor using QR Token (JUDGE, MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-operation/check-in")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN")]
    public async Task<IActionResult> CheckIn([FromBody] CubeNexus.Application.DTOs.Registration.CheckInRequestDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _checkInUseCase.ExecuteAsync(dto);

            // Push real-time notification to competitor's mobile via SignalR
            // The competitor listens on group "competitor:{registrationId}"
            if (result.Success)
            {
                var competitorGroup = $"competitor:{result.RegistrationId}";
                await _hubContext.Clients.Group(competitorGroup).SendAsync(
                    "CompetitorCheckedIn",
                    new
                    {
                        RegistrationId    = result.RegistrationId,
                        PlayerName        = result.PlayerName,
                        TournamentName    = result.TournamentName,
                        AlreadyCheckedIn  = result.AlreadyCheckedIn,
                        CheckedInAt       = result.CheckedInAt,
                        Assignments       = result.Assignments   // includes GroupName + StationNumber per event
                    },
                    ct);
            }

            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while checking in.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Đóng đăng ký một Event trong Tournament (MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-management/events/{eventId:guid}/close-registration")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> CloseRegistration(Guid eventId, CancellationToken ct)
    {
        try
        {
            var result = await _operationService.CloseEventRegistrationAsync(eventId, ct);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
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
    /// Sinh các Group và gán Station xoay vòng cho các competitor đã đăng ký (MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-management/events/{eventId:guid}/groups")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> GenerateGroups(Guid eventId, [FromBody] GenerateGroupsDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _operationService.GenerateEventGroupsAsync(eventId, dto, ct);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
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
            return StatusCode(500, new { message = "An error occurred while generating groups.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Sinh các scramble cho tất cả các Group thuộc một Round cụ thể của Event (MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-management/events/{eventId:guid}/scrambles")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> GenerateScrambles(Guid eventId, [FromBody] GenerateScramblesDto dto, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var result = await _operationService.GenerateGroupScramblesAsync(eventId, dto, userId, ct);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
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
            return StatusCode(500, new { message = "An error occurred while generating scrambles.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Trọng tài nhập kết quả Traditional (JUDGE, MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-operation/results/traditional")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN")]
    public async Task<IActionResult> SubmitTraditionalResult([FromBody] SubmitTraditionalResultDto dto, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var result = await _operationService.SubmitTraditionalResultAsync(dto, userId, ct);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
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
            return StatusCode(500, new { message = "An error occurred while submitting traditional result.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Trọng tài nhập kết quả Medley (JUDGE, MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-operation/results/medley")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN")]
    public async Task<IActionResult> SubmitMedleyResult([FromBody] SubmitMedleyResultDto dto, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var result = await _operationService.SubmitMedleyResultAsync(dto, userId, ct);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
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
            return StatusCode(500, new { message = "An error occurred while submitting medley result.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Chuyển tiếp (Advance) các đấu thủ có thành tích cao từ một vòng sang vòng tiếp theo (MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-operation/events/{eventId:guid}/rounds/{roundNumber:int}/advance")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> AdvanceRound(Guid eventId, int roundNumber, [FromBody] AdvanceRoundRequestDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _advanceRoundUseCase.ExecuteAsync(eventId, roundNumber, dto);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while advancing to the next round.", detail = ex.Message });
        }
    }


    /// <summary>
    /// Lấy mã QR ticket của competitor đã đăng ký giải đấu (COMPETITOR, JUDGE, MANAGER, ADMIN).
    /// </summary>
    [HttpGet("api/tournament-operation/competitor/qr-ticket")]
    [Authorize]
    public async Task<IActionResult> GetCompetitorQrTicket([FromQuery] Guid tournamentId, CancellationToken ct)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out var userId))
                return Unauthorized(new { message = "Invalid user token." });

            var registration = await _unitOfWork.Registrations.FirstOrDefaultAsync(
                r => r.TournamentId == tournamentId && r.UserId == userId,
                ct
            );

            if (registration == null)
                return NotFound(new { message = "You are not registered for this tournament." });

            return Ok(new
            {
                RegistrationId = registration.Id,
                QrToken = registration.QrToken,
                CheckedInAt = registration.CheckedInAt,
                StatusCode = registration.StatusCode
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Lấy danh sách thí sinh đã CONFIRMED/CHECKED_IN của giải đấu dành cho Check-in Desk (JUDGE, MANAGER, ADMIN).
    /// </summary>
    [HttpGet("api/tournament-operation/judge/check-in-roster")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN")]
    public async Task<IActionResult> GetCheckInRoster([FromQuery] Guid tournamentId, CancellationToken ct)
    {
        try
        {
            var registrations = await _unitOfWork.Registrations.FindAsync(
                r => r.TournamentId == tournamentId &&
                     (r.StatusCode == "CONFIRMED" || r.StatusCode == "CHECKED_IN"),
                ct
            );

            // Batch load user display names
            var userIds = registrations.Select(r => r.UserId).Distinct().ToList();
            var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id), ct);
            var userMap = users.ToDictionary(u => u.Id, u => u.DisplayName ?? u.Email ?? "-");

            var result = registrations
                .OrderBy(r => r.StatusCode == "CHECKED_IN" ? 0 : 1)
                .ThenBy(r => userMap.GetValueOrDefault(r.UserId, "-"))
                .Select(r => new
                {
                    RegistrationId = r.Id,
                    CompetitorName = userMap.GetValueOrDefault(r.UserId, "-"),
                    StatusCode = r.StatusCode,
                    CheckedInAt = r.CheckedInAt,
                    IsCheckedIn = r.CheckedInAt.HasValue || r.StatusCode == "CHECKED_IN",
                })
                .ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching check-in roster.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Xác thực thông tin đấu thủ qua mã QR và tự động resolve GroupId theo trạm của trọng tài (JUDGE, MANAGER, ADMIN).

    /// </summary>
    [HttpPost("api/tournament-operation/judge/verify-by-station")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN")]
    public async Task<IActionResult> VerifyJudgeStationByStation([FromBody] VerifyJudgeStationByStationDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _verifyJudgeStationByStationUseCase.ExecuteAsync(dto, ct);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while verifying the competitor at the station.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Load roster assigned to a specific Group/Station for station judge flow (JUDGE, MANAGER, ADMIN).
    /// </summary>
    [HttpGet("api/tournament-operation/judge/station-roster")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN")]
    public async Task<IActionResult> GetJudgeStationRoster(
        [FromQuery] Guid eventId,
        [FromQuery] int roundNumber,
        [FromQuery] int stationNumber,
        [FromQuery] int groupNumber = 0,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _operationService.GetJudgeStationRosterAsync(eventId, roundNumber, groupNumber, stationNumber, ct);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while loading station roster.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Lấy tiến trình giải (solve progress) và thông tin scramble tiếp theo của đấu thủ (JUDGE, MANAGER, ADMIN).
    /// </summary>
    [HttpGet("api/tournament-operation/competitors/{groupCompetitorId:guid}/solve-progress")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN")]
    public async Task<IActionResult> GetSolveProgress(Guid groupCompetitorId, CancellationToken ct)
    {
        try
        {
            var result = await _operationService.GetSolveProgressAsync(groupCompetitorId, ct);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching solve progress.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Hiệu chỉnh kết quả thi đấu (MANAGER, ADMIN).
    /// </summary>
    [HttpPatch("api/tournament-operation/results/{resultId:guid}/correction")]
    [Authorize(Roles = "MANAGER,ADMIN")]
    public async Task<IActionResult> CorrectResult(Guid resultId, [FromBody] ResultCorrectionDto dto)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdString, out var managerId))
            {
                return Unauthorized(new { message = "Invalid user token." });
            }

            var result = await _correctResultUseCase.ExecuteAsync(resultId, dto, managerId);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while correcting the result.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Lấy danh sách Scramble của một Group cụ thể (MANAGER, ADMIN, JUDGE).
    /// </summary>
    [HttpGet("api/tournament-operation/groups/{groupId:guid}/scrambles")]
    [Authorize(Roles = "MANAGER,ADMIN,JUDGE")]
    public async Task<IActionResult> GetGroupScrambles(Guid groupId, CancellationToken ct)
    {
        try
        {
            var scrambles = await _operationService.GetGroupScramblesAsync(groupId, ct);
            return Ok(scrambles);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching group scrambles.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Lấy danh sách các loại Penalty (JUDGE, MANAGER, ADMIN, COMPETITOR).
    /// </summary>
    [HttpGet("api/tournament-operation/penalty-types")]
    [Authorize]
    public async Task<IActionResult> GetPenaltyTypes(CancellationToken ct)
    {
        try
        {
            var penaltyTypes = await _operationService.GetPenaltyTypesAsync(ct);
            return Ok(penaltyTypes);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while fetching penalty types.", detail = ex.Message });
        }
    }

    private IActionResult HandleCustomException(CubeNexus.Application.Exceptions.CustomException ex)
    {
        var response = new Dictionary<string, object>
        {
            { "code", ex.ErrorCode },
            { "errorCode", ex.ErrorCode },
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
