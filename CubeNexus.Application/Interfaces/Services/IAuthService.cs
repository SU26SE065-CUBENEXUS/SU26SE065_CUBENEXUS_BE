using CubeNexus.Application.DTOs.Auth;

namespace CubeNexus.Application.Interfaces.Services;

public interface IAuthService
{
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
    Task<RegisterResponseDto> RegisterAsync(
        RegisterRequestDto request,
        Stream? avatarStream = null,
        string? avatarContentType = null,
        string? avatarFileName = null,
        CancellationToken cancellationToken = default);
    Task<UserProfileDto> GetProfileAsync(Guid userId);
    Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequestDto request);
    Task<UserProfileDto> UploadAvatarAsync(Guid userId, Stream contentStream, string? contentType, string? fileName = null, CancellationToken cancellationToken = default);
    Task<LoginResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request);
    Task<ForgotPasswordResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto request);
    Task<MessageResponseDto> VerifyOtpAsync(VerifyOtpRequestDto request);
    Task<MessageResponseDto> ResetPasswordAsync(ResetPasswordRequestDto request);
    Task<MessageResponseDto> ChangePasswordAsync(Guid userId, ChangePasswordRequestDto request);
    Task<MessageResponseDto> LogoutAsync(Guid userId, LogoutRequestDto? request = null);
}
