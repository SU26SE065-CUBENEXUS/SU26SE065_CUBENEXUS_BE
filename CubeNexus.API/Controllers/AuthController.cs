using System.Security.Claims;
using CubeNexus.Application.DTOs.Auth;
using CubeNexus.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Đăng ký tài khoản. multipart/form-data.
    /// Có thể kèm file ảnh (field: file) lúc đăng ký, hoặc bỏ trống rồi upload sau qua Upload-Avatar.
    /// </summary>
    [HttpPost("register")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Register(
        [FromForm] RegisterRequestDto request,
        IFormFile? file,
        CancellationToken ct)
    {
        try
        {
            if (file != null && file.Length > 5 * 1024 * 1024)
                return BadRequest(new { message = "Ảnh đại diện không được vượt quá 5MB." });

            if (file != null && file.Length > 0)
            {
                await using var stream = file.OpenReadStream();
                var result = await _authService.RegisterAsync(
                    request, stream, file.ContentType, file.FileName, ct);
                return Ok(result);
            }

            var resultWithoutAvatar = await _authService.RegisterAsync(request, cancellationToken: ct);
            return Ok(resultWithoutAvatar);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequestDto request)
    {
        try
        {
            var result = await _authService.RefreshTokenAsync(request);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto? request = null)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized(new { message = "Token không hợp lệ." });

        var result = await _authService.LogoutAsync(userId, request);
        return Ok(result);
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
    {
        try
        {
            var result = await _authService.ForgotPasswordAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequestDto request)
    {
        try
        {
            var result = await _authService.VerifyOtpAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request)
    {
        try
        {
            var result = await _authService.ResetPasswordAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Lấy thông tin hồ sơ của tài khoản đang đăng nhập.
    /// </summary>
    [Authorize]
    [HttpGet("My-Profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized(new { message = "Token không hợp lệ." });

        try
        {
            var result = await _authService.GetProfileAsync(userId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật hồ sơ cá nhân. FE gửi các field cần sửa (có thể gửi cả form đầy đủ).
    /// Field nào không gửi / null thì giữ nguyên giá trị hiện tại.
    /// </summary>
    [Authorize]
    [HttpPut("Update-Profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto request)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized(new { message = "Token không hợp lệ." });

        try
        {
            var result = await _authService.UpdateProfileAsync(userId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Upload ảnh đại diện lên R2 và cập nhật AvatarUrl của tài khoản đang đăng nhập.
    /// multipart/form-data, field name: file (mọi đuôi file, tối đa 5MB).
    /// </summary>
    [Authorize]
    [HttpPost("Upload-Avatar")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized(new { message = "Token không hợp lệ." });

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Vui lòng chọn file để upload." });

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "Ảnh đại diện không được vượt quá 5MB." });

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _authService.UploadAvatarAsync(userId, stream, file.ContentType, file.FileName, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized(new { message = "Token không hợp lệ." });

        try
        {
            var result = await _authService.ChangePasswordAsync(userId, request);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
