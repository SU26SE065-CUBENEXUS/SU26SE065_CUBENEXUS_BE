namespace CubeNexus.Application.Interfaces.Services;

public interface IEmailService
{
    Task SendEmailConfirmationAsync(string toEmail, string displayName, string token);
    Task SendPasswordResetAsync(string toEmail, string displayName, string token);
    Task SendPasswordChangedNotificationAsync(string toEmail, string displayName);
}
