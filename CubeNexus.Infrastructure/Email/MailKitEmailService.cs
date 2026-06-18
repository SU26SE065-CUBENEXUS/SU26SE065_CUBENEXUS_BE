using System.Net;

using System.Text;

using CubeNexus.Application.Interfaces.Services;

using MailKit.Net.Smtp;

using MailKit.Security;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Options;

using MimeKit;



namespace CubeNexus.Infrastructure.Email;



public class MailKitEmailService : IEmailService

{

    private readonly EmailSettings _settings;
    private readonly ILogger<MailKitEmailService> _logger;
    private readonly IHostEnvironment _environment;

    public MailKitEmailService(
        IOptions<EmailSettings> settings,
        ILogger<MailKitEmailService> logger,
        IHostEnvironment environment)
    {
        _settings = settings.Value;
        _logger = logger;
        _environment = environment;
    }



    public Task SendEmailConfirmationAsync(string toEmail, string displayName, string token)

    {

        var link = BuildLink(_settings.EmailConfirmationPath, toEmail, token);

        var subject = "Xác nhận tài khoản CubeNexus";

        var body = $"""

            <p>Xin chào <strong>{WebUtility.HtmlEncode(displayName)}</strong>,</p>

            <p>Cảm ơn bạn đã đăng ký tài khoản CubeNexus. Vui lòng nhấn vào liên kết bên dưới để xác nhận email:</p>

            <p><a href="{WebUtility.HtmlEncode(link)}">Xác nhận email</a></p>

            <p>Liên kết có hiệu lực trong {_settings.EmailConfirmationExpirationHours} giờ.</p>

            <p>Nếu bạn không tạo tài khoản này, hãy bỏ qua email.</p>

            """;



        return SendAsync(toEmail, subject, body);

    }



    public Task SendPasswordResetAsync(string toEmail, string displayName, string token)

    {

        var link = BuildLink(_settings.PasswordResetPath, toEmail, token);

        if (_environment.IsDevelopment())
            _logger.LogWarning("[DEV] Link đặt lại mật khẩu cho {Email}: {Link}", toEmail, link);

        var subject = "Đặt lại mật khẩu CubeNexus";

        var body = $"""

            <p>Xin chào <strong>{WebUtility.HtmlEncode(displayName)}</strong>,</p>

            <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.</p>

            <p><a href="{WebUtility.HtmlEncode(link)}">Đặt lại mật khẩu</a></p>

            <p>Liên kết có hiệu lực trong {_settings.PasswordResetExpirationHours} giờ.</p>

            <p>Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email.</p>

            """;



        return SendAsync(toEmail, subject, body);

    }



    public Task SendPasswordChangedNotificationAsync(string toEmail, string displayName)

    {

        var subject = "Mật khẩu CubeNexus đã được thay đổi";

        var body = $"""

            <p>Xin chào <strong>{WebUtility.HtmlEncode(displayName)}</strong>,</p>

            <p>Mật khẩu tài khoản CubeNexus của bạn vừa được thay đổi.</p>

            <p>Nếu bạn không thực hiện thay đổi này, hãy liên hệ hỗ trợ ngay.</p>

            """;



        return SendAsync(toEmail, subject, body);

    }



    private string BuildLink(string path, string email, string token)

    {

        var baseUrl = _settings.FrontendBaseUrl.TrimEnd('/');

        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";

        var query = $"token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";

        return $"{baseUrl}{normalizedPath}?{query}";

    }



    private async Task SendAsync(string toEmail, string subject, string htmlBody)

    {

        if (string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))

        {

            throw new InvalidOperationException(

                "EmailSettings chưa được cấu hình. Hãy đặt Username và Password (App Password Gmail) qua user-secrets.");

        }



        var fromEmail = string.IsNullOrWhiteSpace(_settings.FromEmail)

            ? _settings.Username

            : _settings.FromEmail;



        try

        {

            var message = new MimeMessage();

            message.From.Add(new MailboxAddress(_settings.FromName, fromEmail));

            message.To.Add(MailboxAddress.Parse(toEmail));

            message.Subject = subject;

            message.Body = new TextPart("html")
            {
                Text = htmlBody
            };

            using var client = new SmtpClient();

            var secureSocketOptions = _settings.UseStartTls

                ? SecureSocketOptions.StartTls

                : SecureSocketOptions.Auto;



            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secureSocketOptions);



            if (client.Capabilities.HasFlag(SmtpCapabilities.Authentication))

            {

                await client.AuthenticateAsync(Encoding.UTF8, _settings.Username, _settings.Password);

            }



            await client.SendAsync(message);

            await client.DisconnectAsync(true);



            _logger.LogInformation("Đã gửi email '{Subject}' tới {Email}.", subject, toEmail);

        }

        catch (Exception ex)

        {

            _logger.LogError(ex, "Gửi email '{Subject}' tới {Email} thất bại.", subject, toEmail);

            throw new InvalidOperationException(

                "Không thể gửi email. Kiểm tra cấu hình Gmail SMTP và App Password.", ex);

        }

    }

}


