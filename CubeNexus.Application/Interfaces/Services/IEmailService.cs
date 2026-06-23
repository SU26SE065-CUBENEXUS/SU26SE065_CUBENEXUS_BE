namespace CubeNexus.Application.Interfaces.Services;

public interface IEmailService
{
    Task SendPasswordResetOtpAsync(
        string toEmail,
        string displayName,
        string otp,
        int expiresInMinutes);
}
