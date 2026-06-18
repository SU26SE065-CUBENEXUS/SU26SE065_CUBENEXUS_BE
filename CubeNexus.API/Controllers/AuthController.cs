using System.Security.Claims;

using CubeNexus.API.Helpers;

using CubeNexus.Infrastructure.Identity;
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



    [HttpPost("register")]

    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)

    {

        try

        {

            var result = await _authService.RegisterAsync(request);

            return Ok(result);

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



    [HttpGet("confirm-email")]

    public async Task<IActionResult> ConfirmEmailGet([FromQuery] string email, [FromQuery] string token)

    {

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))

            return AuthHtmlPages.Page("Lỗi xác nhận", "<p>Thiếu email hoặc token.</p>", isError: true);



        try

        {

            var result = await _authService.ConfirmEmailAsync(new ConfirmEmailRequestDto

            {

                Email = email,

                Token = token

            });



            return AuthHtmlPages.Page("Xác nhận thành công", $"<p>{result.Message}</p>");

        }

        catch (InvalidOperationException ex)

        {

            return AuthHtmlPages.Page("Lỗi xác nhận", $"<p>{ex.Message}</p>", isError: true);

        }

    }



    [HttpPost("confirm-email")]

    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequestDto request)

    {

        try

        {

            var result = await _authService.ConfirmEmailAsync(request);

            return Ok(result);

        }

        catch (InvalidOperationException ex)

        {

            return BadRequest(new { message = ex.Message });

        }

    }



    [HttpPost("resend-confirmation")]

    public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequestDto request)

    {

        try

        {

            var result = await _authService.ResendConfirmationAsync(request);

            return Ok(result);

        }

        catch (InvalidOperationException ex)

        {

            return BadRequest(new { message = ex.Message });

        }

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



    [HttpGet("reset-password")]

    public IActionResult ResetPasswordGet([FromQuery] string email, [FromQuery] string token)

    {

        email = AuthTokenNormalizer.NormalizeEmail(email);

        token = AuthTokenNormalizer.NormalizeToken(token);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))

            return AuthHtmlPages.Page("Lỗi", "<p>Thiếu email hoặc token.</p>", isError: true);



        return AuthHtmlPages.ResetPasswordForm(email, token);

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


