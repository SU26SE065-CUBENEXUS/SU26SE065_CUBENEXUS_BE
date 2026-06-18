namespace CubeNexus.Infrastructure.Email;

public class EmailSettings
{
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "CubeNexus";
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public string EmailConfirmationPath { get; set; } = "/confirm-email";
    public string PasswordResetPath { get; set; } = "/reset-password";
    public int EmailConfirmationExpirationHours { get; set; } = 24;
    public int PasswordResetExpirationHours { get; set; } = 1;
}
