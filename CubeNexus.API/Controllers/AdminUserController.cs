using CubeNexus.Application.DTOs.Admin;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "ADMIN")]
public class AdminUserController : ControllerBase
{
    private readonly IAdminUserService _adminUserService;

    public AdminUserController(IAdminUserService adminUserService)
    {
        _adminUserService = adminUserService;
    }

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
        UnauthorizedAccessException forbidden => StatusCode(StatusCodes.Status403Forbidden, new { code = "FORBIDDEN", message = forbidden.Message }),
        KeyNotFoundException notFound => NotFound(new { code = "NOT_FOUND", message = notFound.Message }),
        InvalidOperationException invalidOperation => BadRequest(new { code = "BAD_REQUEST", message = invalidOperation.Message }),
        ArgumentException argument => BadRequest(new { code = "BAD_REQUEST", message = argument.Message }),
        _ => StatusCode(StatusCodes.Status500InternalServerError, new { code = "SERVER_ERROR", message = ex.Message })
    };

    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _adminUserService.GetUsersAsync(page, pageSize, search, role, status, ct));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> GetUserById(
        Guid userId,
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _adminUserService.GetUserByIdAsync(userId, ct));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPut("{userId:guid}/role")]
    public async Task<IActionResult> UpdateUserRole(
        Guid userId,
        [FromBody] UpdateUserRoleRequestDto request,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var adminId)) return Unauthorized401();

        try
        {
            return Ok(await _adminUserService.UpdateUserRoleAsync(adminId, userId, request.UserRole, ct));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{userId:guid}/ban")]
    public async Task<IActionResult> BanUser(
        Guid userId,
        [FromBody] BanUserRequestDto request,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var adminId)) return Unauthorized401();

        try
        {
            return Ok(await _adminUserService.BanUserAsync(adminId, userId, request, ct));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPost("{userId:guid}/unban")]
    public async Task<IActionResult> UnbanUser(
        Guid userId,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var adminId)) return Unauthorized401();

        try
        {
            return Ok(await _adminUserService.UnbanUserAsync(userId, ct));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }
}
