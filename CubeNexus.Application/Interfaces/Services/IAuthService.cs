using CubeNexus.Application.DTOs.Auth;

namespace CubeNexus.Application.Interfaces.Services;

public interface IAuthService
{
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
    Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request);
    Task<LoginResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request);
    Task<MessageResponseDto> ConfirmEmailAsync(ConfirmEmailRequestDto request);
    Task<MessageResponseDto> ResendConfirmationAsync(ResendConfirmationRequestDto request);
    Task<MessageResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto request);
    Task<MessageResponseDto> ResetPasswordAsync(ResetPasswordRequestDto request);
    Task<MessageResponseDto> ChangePasswordAsync(Guid userId, ChangePasswordRequestDto request);
}
