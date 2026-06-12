using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CubeNexus.Application.DTOs.Operation;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IVerifyJudgeStationUseCase _verifyJudgeStationUseCase;
    private readonly CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICorrectResultUseCase _correctResultUseCase;

    public TournamentOperationController(
        ITournamentOperationService operationService,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICheckInRegistrationByQrUseCase checkInUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IStartRoundUseCase startRoundUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ILockRoundResultsUseCase lockRoundResultsUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteRoundUseCase completeRoundUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICompleteEventUseCase completeEventUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IAdvanceRoundUseCase advanceRoundUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.IVerifyJudgeStationUseCase verifyJudgeStationUseCase,
        CubeNexus.Application.Interfaces.UseCases.TournamentOperation.ICorrectResultUseCase correctResultUseCase)
    {
        _operationService = operationService;
        _checkInUseCase = checkInUseCase;
        _startRoundUseCase = startRoundUseCase;
        _lockRoundResultsUseCase = lockRoundResultsUseCase;
        _completeRoundUseCase = completeRoundUseCase;
        _completeEventUseCase = completeEventUseCase;
        _advanceRoundUseCase = advanceRoundUseCase;
        _verifyJudgeStationUseCase = verifyJudgeStationUseCase;
        _correctResultUseCase = correctResultUseCase;
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
    /// Xác thực thông tin đấu thủ qua mã QR tại trạm của trọng tài (JUDGE, MANAGER, ADMIN).
    /// </summary>
    [HttpPost("api/tournament-operation/judge/verify")]
    [Authorize(Roles = "JUDGE,MANAGER,ADMIN")]
    public async Task<IActionResult> VerifyJudgeStation([FromBody] VerifyJudgeStationDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _verifyJudgeStationUseCase.ExecuteAsync(dto);
            return Ok(result);
        }
        catch (CubeNexus.Application.Exceptions.CustomException ex)
        {
            return HandleCustomException(ex);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while verifying the judge station.", detail = ex.Message });
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
