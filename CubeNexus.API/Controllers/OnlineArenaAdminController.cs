using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.UseCases.OnlineArena;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/admin/fraud-reports")]
[Route("api/admin/online/fraud-reports")]
[Authorize(Roles = "ADMIN,MANAGER")]
public class OnlineArenaAdminController : ControllerBase
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

    [HttpGet]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingReports([FromServices] GetPendingFraudReportsUseCase useCase)
    {
        try
        {
            return Ok(await useCase.ExecuteAsync());
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{reportId:guid}")]
    public async Task<IActionResult> GetReportDetail(Guid reportId, [FromServices] GetFraudReportDetailUseCase useCase)
    {
        try
        {
            return Ok(await useCase.ExecuteAsync(reportId));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{reportId:guid}/review")]
    public async Task<IActionResult> ReviewReport(Guid reportId, [FromBody] ReviewFraudReportRequest request, [FromServices] ReviewFraudReportUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var reviewerId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(reviewerId, reportId, request));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{reportId:guid}/resolve")]
    public async Task<IActionResult> ResolveReport(Guid reportId, [FromBody] ResolveFraudReportRequest request, [FromServices] ReviewFraudReportUseCase useCase)
    {
        if (!TryGetCurrentUserId(out var reviewerId)) return Unauthorized401();

        try
        {
            return Ok(await useCase.ExecuteAsync(reviewerId, reportId, new ReviewFraudReportRequest
            {
                VerdictCode = request.Decision,
                AdminNote = request.AdminNote
            }));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }
}
