using System.Collections.Concurrent;
using System.Net;
using System.Text;
using CubeNexus.Application.Interfaces.Services;
using HandlebarsDotNet;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CubeNexus.Infrastructure.Email;

public class MailKitEmailService : IEmailService
{
    private const string PasswordResetTemplateName = "password-reset";

    private readonly EmailSettings _settings;
    private readonly ILogger<MailKitEmailService> _logger;
    private readonly IHostEnvironment _environment;
    private readonly bool _smtpConfigured;
    private readonly ConcurrentDictionary<string, HandlebarsTemplate<object, object>> _templateCache = new();

    public MailKitEmailService(
        IOptions<EmailSettings> settings,
        ILogger<MailKitEmailService> logger,
        IHostEnvironment environment)
    {
        _settings = settings.Value;
        _logger = logger;
        _environment = environment;
        _smtpConfigured = _settings.IsSmtpConfigured;

        if (!_smtpConfigured)
        {
            _logger.LogWarning(
                "SMTP chưa được cấu hình đầy đủ. Email sẽ fallback sang chế độ log preview [MAIL_PREVIEW].");
        }
    }

    public Task SendPasswordResetOtpAsync(
        string toEmail,
        string displayName,
        string otp,
        int expiresInMinutes)
    {
        if (_environment.IsDevelopment())
            _logger.LogWarning("[DEV] OTP đặt lại mật khẩu cho {Email}: {Otp}", toEmail, otp);

        var subject = "Mã OTP đặt lại mật khẩu CubeNexus";
        var text =
            $"Xin chào {displayName}, mã OTP đặt lại mật khẩu của bạn là {otp}. " +
            $"Mã có hiệu lực trong {expiresInMinutes} phút. " +
            "Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này.";

        var html = RenderTemplate(PasswordResetTemplateName, new
        {
            displayName = WebUtility.HtmlEncode(displayName),
            code = WebUtility.HtmlEncode(otp),
            expiresInMinutes
        });

        return SendMailAsync(toEmail, subject, text, html);
    }

    private string RenderTemplate(string templateName, object context)
    {
        var template = _templateCache.GetOrAdd(templateName, static name =>
        {
            var templatePath = Path.Combine(
                AppContext.BaseDirectory,
                "Email",
                "Templates",
                $"{name}.hbs");

            if (!File.Exists(templatePath))
            {
                throw new FileNotFoundException($"Không tìm thấy email template: {templatePath}");
            }

            var templateSource = File.ReadAllText(templatePath);
            return Handlebars.Compile(templateSource);
        });

        return template(context);
    }

    private async Task SendMailAsync(string toEmail, string subject, string textBody, string htmlBody)
    {
        if (!_smtpConfigured)
        {
            _logger.LogInformation(
                "[MAIL_PREVIEW] to={Email} subject=\"{Subject}\" text=\"{Text}\"",
                toEmail,
                subject,
                textBody);
            return;
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
            message.Body = new Multipart("alternative")
            {
                new TextPart("plain") { Text = textBody },
                new TextPart("html") { Text = htmlBody }
            };

            using var client = new SmtpClient();

            var secureSocketOptions = _settings.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.Auto;

            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secureSocketOptions);

            if (client.Capabilities.HasFlag(SmtpCapabilities.Authentication))
                await client.AuthenticateAsync(Encoding.UTF8, _settings.Username, _settings.Password);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Đã gửi email '{Subject}' tới {Email}.", subject, toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gửi email '{Subject}' tới {Email} thất bại.", subject, toEmail);
            throw new InvalidOperationException(
                "Không thể gửi email. Kiểm tra cấu hình SMTP trong appsettings.", ex);
        }
    }
}
