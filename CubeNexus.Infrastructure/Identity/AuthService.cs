using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CubeNexus.Application.DTOs.Auth;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Domain.Enums;
using CubeNexus.Infrastructure.Email;
using CubeNexus.Infrastructure.Identity;
using CubeNexus.Infrastructure.Options;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CubeNexus.Infrastructure.Identity;

public partial class AuthService : IAuthService
{
    private const long MaxAvatarBytes = 5 * 1024 * 1024; // 5 MB

    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IHostEnvironment _environment;
    private readonly IOnlineProfileInitService _profileInitService;
    private readonly IRecordingStorageService _storageService;
    private readonly JwtSettings _jwtSettings;
    private readonly EmailSettings _emailSettings;
    private readonly R2Options _r2Options;

    public AuthService(
        ApplicationDbContext context,
        IEmailService emailService,
        IHostEnvironment environment,
        IOnlineProfileInitService profileInitService,
        IRecordingStorageService storageService,
        IOptions<JwtSettings> jwtSettings,
        IOptions<EmailSettings> emailSettings,
        IOptions<R2Options> r2Options)
    {
        _context = context;
        _emailService = emailService;
        _environment = environment;
        _profileInitService = profileInitService;
        _storageService = storageService;
        _jwtSettings = jwtSettings.Value;
        _emailSettings = emailSettings.Value;
        _r2Options = r2Options.Value;
    }

    public async Task<RegisterResponseDto> RegisterAsync(
        RegisterRequestDto request,
        Stream? avatarStream = null,
        string? avatarContentType = null,
        string? avatarFileName = null,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileFields(request.DisplayName, request.Phone, request.Address);

        if (string.IsNullOrWhiteSpace(request.Email))
            throw new InvalidOperationException("Email không được để trống.");

        if (string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException("Mật khẩu không được để trống.");

        if (avatarStream != null && avatarStream.CanSeek && avatarStream.Length > MaxAvatarBytes)
            throw new InvalidOperationException("Ảnh đại diện không được vượt quá 5MB.");

        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (existingUser != null)
            throw new InvalidOperationException("Email đã được sử dụng.");

        var userCode = await GenerateUniqueUserCodeAsync();
        var now = DateTime.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserCode = userCode,
            Email = request.Email.Trim(),
            PasswordHash = HashPassword(request.Password),
            DisplayName = request.DisplayName.Trim(),
            Phone = request.Phone.Trim(),
            Address = request.Address.Trim(),
            AvatarUrl = null,
            UserRole = "COMPETITOR",
            IsActive = true,
            IsBanned = false,
            EmailConfirmed = true,
            EmailConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _context.Users.AddAsync(user, cancellationToken);
        await _profileInitService.EnsureStandardProfileAsync(user.Id);
        await _context.SaveChangesAsync(cancellationToken);

        if (avatarStream != null)
        {
            user.AvatarUrl = await UploadAvatarToR2Async(
                user.Id, avatarStream, avatarContentType, avatarFileName, cancellationToken);
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new RegisterResponseDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Phone = user.Phone,
            Address = user.Address,
            AvatarUrl = user.AvatarUrl
        };
    }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("Người dùng không tồn tại.");

        return ToProfileDto(user);
    }

    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequestDto request)
    {
        var hasAnyField =
            request.DisplayName != null ||
            request.Phone != null ||
            request.Address != null ||
            request.AvatarUrl != null;

        if (!hasAnyField)
            throw new InvalidOperationException("Không có thông tin nào để cập nhật.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("Người dùng không tồn tại.");

        if (request.DisplayName != null)
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
                throw new InvalidOperationException("Tên hiển thị không được để trống.");
            user.DisplayName = request.DisplayName.Trim();
        }

        if (request.Phone != null)
        {
            if (string.IsNullOrWhiteSpace(request.Phone))
                throw new InvalidOperationException("Số điện thoại không được để trống.");

            var phone = request.Phone.Trim();
            if (phone.Length < 9 || phone.Length > 20)
                throw new InvalidOperationException("Số điện thoại không hợp lệ.");

            user.Phone = phone;
        }

        if (request.Address != null)
        {
            if (string.IsNullOrWhiteSpace(request.Address))
                throw new InvalidOperationException("Địa chỉ không được để trống.");
            user.Address = request.Address.Trim();
        }

        if (request.AvatarUrl != null)
        {
            user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl)
                ? null
                : request.AvatarUrl.Trim();
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return ToProfileDto(user);
    }

    public async Task<UserProfileDto> UploadAvatarAsync(
        Guid userId,
        Stream contentStream,
        string? contentType,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("Người dùng không tồn tại.");

        user.AvatarUrl = await UploadAvatarToR2Async(userId, contentStream, contentType, fileName, cancellationToken);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return ToProfileDto(user);
    }

    private async Task<string> UploadAvatarToR2Async(
        Guid userId,
        Stream contentStream,
        string? contentType,
        string? fileName,
        CancellationToken cancellationToken)
    {
        if (contentStream == null || !contentStream.CanRead)
            throw new InvalidOperationException("File không hợp lệ.");

        var extension = ResolveFileExtension(fileName);
        var normalizedContentType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType.Split(';', 2)[0].Trim();

        if (contentStream.CanSeek && contentStream.Length > MaxAvatarBytes)
            throw new InvalidOperationException("Ảnh đại diện không được vượt quá 5MB.");

        await using var buffer = new MemoryStream();
        await contentStream.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length == 0)
            throw new InvalidOperationException("File không được để trống.");

        if (buffer.Length > MaxAvatarBytes)
            throw new InvalidOperationException("Ảnh đại diện không được vượt quá 5MB.");

        buffer.Position = 0;
        var objectKey = string.IsNullOrEmpty(extension)
            ? $"avatars/{userId:D}/{Guid.NewGuid():N}"
            : $"avatars/{userId:D}/{Guid.NewGuid():N}.{extension}";
        await _storageService.UploadStreamAsync(objectKey, buffer, normalizedContentType, cancellationToken);

        var publicUrl = _r2Options.GetPublicUrl(objectKey);
        if (string.IsNullOrWhiteSpace(publicUrl))
            throw new InvalidOperationException("R2 PublicUrl chưa được cấu hình. Không thể tạo URL ảnh đại diện.");

        return publicUrl;
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        var identifier = (request.Email ?? string.Empty).Trim().ToLower();
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == identifier || (u.UserCode != null && u.UserCode.ToLower() == identifier));

        if (user == null)
            throw new UnauthorizedAccessException("Email hoặc mật khẩu không đúng.");

        if (!VerifyPassword(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Email hoặc mật khẩu không đúng.");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Tài khoản đã bị vô hiệu hóa.");

        if (user.IsBanned)
        {
            if (user.BannedUntil.HasValue && user.BannedUntil.Value <= DateTime.UtcNow)
            {
                user.IsBanned = false;
                user.BanReason = null;
                user.BannedAt = null;
                user.BannedUntil = null;
                await _context.SaveChangesAsync();
            }
            else
            {
                var reason = string.IsNullOrWhiteSpace(user.BanReason) ? "Không có lý do cụ thể." : user.BanReason;
                var untilStr = user.BannedUntil.HasValue
                    ? $"đến {user.BannedUntil.Value.ToLocalTime():dd/MM/yyyy HH:mm}"
                    : "vĩnh viễn";
                throw new UnauthorizedAccessException($"Tài khoản của bạn đã bị cấm ({untilStr}). Lý do: {reason}");
            }
        }

        return await GenerateTokenResponseAsync(user);
    }

    public async Task<LoginResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request)
    {
        var refreshToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        if (refreshToken == null)
            throw new UnauthorizedAccessException("Refresh token không hợp lệ.");

        if (!refreshToken.IsActive)
            throw new UnauthorizedAccessException("Refresh token đã hết hạn hoặc bị thu hồi.");

        var user = refreshToken.User;
        refreshToken.RevokedAt = DateTime.UtcNow;

        var tokenResponse = await GenerateTokenResponseAsync(user);
        refreshToken.ReplacedBy = tokenResponse.RefreshToken;
        await _context.SaveChangesAsync();

        return tokenResponse;
    }

    public async Task<ForgotPasswordResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto request)
    {
        var email = AuthTokenNormalizer.NormalizeEmail(request.Email);
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null || !user.IsActive || user.IsBanned)
            throw new InvalidOperationException("Email không tìm thấy, vui lòng nhập lại email");

        var otp = await CreatePasswordResetOtpAsync(user.Id);
        await _context.SaveChangesAsync();

        await _emailService.SendPasswordResetOtpAsync(
            user.Email,
            user.DisplayName,
            otp,
            _emailSettings.OtpExpirationMinutes);

        return new ForgotPasswordResponseDto
        {
            Message = "Chúng tôi đã gửi mã OTP đến email của bạn.",
            DevOtp = _environment.IsDevelopment() ? otp : null,
            EmailSent = _environment.IsDevelopment() ? _emailSettings.IsSmtpConfigured : null
        };
    }

    public async Task<MessageResponseDto> VerifyOtpAsync(VerifyOtpRequestDto request)
    {
        var email = AuthTokenNormalizer.NormalizeEmail(request.Email);
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            throw new InvalidOperationException("Mã OTP không hợp lệ hoặc đã hết hạn.");

        var otp = AuthTokenNormalizer.NormalizeOtp(request.Otp);
        if (!OtpPattern().IsMatch(otp))
            throw new InvalidOperationException("Mã OTP không hợp lệ hoặc đã hết hạn.");

        var otpHash = HashToken(otp);
        var userToken = await _context.UserTokens
            .FirstOrDefaultAsync(t =>
                t.UserId == user.Id &&
                t.TokenType == UserTokenType.PasswordReset &&
                t.TokenHash == otpHash);

        if (userToken == null || !userToken.IsActive)
            throw new InvalidOperationException("Mã OTP không hợp lệ hoặc đã hết hạn.");

        return new MessageResponseDto { Message = "Mã OTP hợp lệ." };
    }

    public async Task<MessageResponseDto> ResetPasswordAsync(ResetPasswordRequestDto request)
    {
        var email = AuthTokenNormalizer.NormalizeEmail(request.Email);
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            throw new InvalidOperationException("Mã OTP không hợp lệ hoặc đã hết hạn.");

        var otp = AuthTokenNormalizer.NormalizeOtp(request.Otp);
        if (!OtpPattern().IsMatch(otp))
            throw new InvalidOperationException("Mã OTP không hợp lệ hoặc đã hết hạn.");

        var otpHash = HashToken(otp);
        var userToken = await _context.UserTokens
            .FirstOrDefaultAsync(t =>
                t.UserId == user.Id &&
                t.TokenType == UserTokenType.PasswordReset &&
                t.TokenHash == otpHash);

        if (userToken == null || !userToken.IsActive)
            throw new InvalidOperationException("Mã OTP không hợp lệ hoặc đã hết hạn.");

        if (request.NewPassword != request.ConfirmNewPassword)
            throw new InvalidOperationException("Mật khẩu xác nhận không khớp.");

        userToken.UsedAt = DateTime.UtcNow;
        user.PasswordHash = HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await RevokeActiveRefreshTokensAsync(user.Id);
        await _context.SaveChangesAsync();

        return new MessageResponseDto { Message = "Đặt lại mật khẩu thành công." };
    }

    public async Task<MessageResponseDto> ChangePasswordAsync(Guid userId, ChangePasswordRequestDto request)
    {
        var user = await _context.Users.FindAsync(userId);

        if (user == null)
            throw new UnauthorizedAccessException("Người dùng không tồn tại.");

        if (!VerifyPassword(request.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Mật khẩu hiện tại không đúng.");

        user.PasswordHash = HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await RevokeActiveRefreshTokensAsync(user.Id);
        await _context.SaveChangesAsync();

        return new MessageResponseDto { Message = "Đổi mật khẩu thành công." };
    }

    public async Task<MessageResponseDto> LogoutAsync(Guid userId, LogoutRequestDto? request = null)
    {
        var now = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            var refreshToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt =>
                    rt.Token == request.RefreshToken &&
                    rt.UserId == userId &&
                    rt.RevokedAt == null &&
                    rt.ExpiresAt > now);

            if (refreshToken != null)
                refreshToken.RevokedAt = now;
        }
        else
        {
            await RevokeActiveRefreshTokensAsync(userId);
        }

        await _context.SaveChangesAsync();

        return new MessageResponseDto { Message = "Đăng xuất thành công." };
    }

    private async Task<string> CreatePasswordResetOtpAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var activeTokens = await _context.UserTokens
            .Where(t => t.UserId == userId && t.TokenType == UserTokenType.PasswordReset && t.UsedAt == null && t.ExpiresAt > now)
            .ToListAsync();

        foreach (var activeToken in activeTokens)
            activeToken.UsedAt = now;

        var otp = GenerateOtp();
        var userToken = new UserToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenType = UserTokenType.PasswordReset,
            TokenHash = HashToken(otp),
            ExpiresAt = now.AddMinutes(_emailSettings.OtpExpirationMinutes),
            CreatedAt = now
        };

        await _context.UserTokens.AddAsync(userToken);
        return otp;
    }

    private async Task RevokeActiveRefreshTokensAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var activeRefreshTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.ExpiresAt > now)
            .ToListAsync();

        foreach (var refreshToken in activeRefreshTokens)
            refreshToken.RevokedAt = now;
    }

    private async Task<LoginResponseDto> GenerateTokenResponseAsync(User user)
    {
        Guid? assignedTournamentId = null;
        string? assignedTournamentName = null;
        string? judgeRoleCode = null;
        int? assignedStationNumber = null;

        if (string.Equals(user.UserRole, "JUDGE", StringComparison.OrdinalIgnoreCase))
        {
            var judgeAssoc = await _context.TournamentJudges
                .Include(tj => tj.Tournament)
                .FirstOrDefaultAsync(tj => tj.UserId == user.Id);
            assignedTournamentId = judgeAssoc?.TournamentId;
            assignedTournamentName = judgeAssoc?.Tournament?.Name;
            judgeRoleCode = judgeAssoc?.RoleCode;
            assignedStationNumber = judgeAssoc?.AssignedStationNumber;
        }

        var accessToken = GenerateAccessToken(user.Id, user.Email, user.DisplayName, user.UserRole.ToUpperInvariant(), assignedTournamentId, assignedStationNumber, judgeRoleCode, assignedTournamentName);
        var refreshToken = GenerateRefreshToken(user.Id);

        await _context.RefreshTokens.AddAsync(refreshToken);
        await _context.SaveChangesAsync();

        return new LoginResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationInMinutes),
            DisplayName = user.DisplayName,
            Email = user.Email,
            UserRole = user.UserRole.ToUpperInvariant(),
            AssignedTournamentId = assignedTournamentId,
            AssignedTournamentName = assignedTournamentName,
            JudgeRoleCode = judgeRoleCode,
            AssignedStationNumber = assignedStationNumber
        };
    }

    private string GenerateAccessToken(Guid userId, string email, string displayName, string userRole, Guid? tournamentId = null, int? stationNumber = null, string? judgeRoleCode = null, string? tournamentName = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claimsList = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("display_name", displayName),
            new Claim(ClaimTypes.Role, userRole.ToUpperInvariant()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (tournamentId.HasValue)
        {
            claimsList.Add(new Claim("tournament_id", tournamentId.Value.ToString()));
        }
        if (!string.IsNullOrWhiteSpace(tournamentName))
        {
            claimsList.Add(new Claim("tournament_name", tournamentName));
        }
        if (stationNumber.HasValue)
        {
            claimsList.Add(new Claim("station_number", stationNumber.Value.ToString()));
        }
        if (!string.IsNullOrEmpty(judgeRoleCode))
        {
            claimsList.Add(new Claim("judge_role", judgeRoleCode));
        }

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claimsList,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationInMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private RefreshToken GenerateRefreshToken(Guid userId)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = Convert.ToBase64String(randomBytes),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationInDays),
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<string> GenerateUniqueUserCodeAsync()
    {
        var random = new Random();
        string userCode;

        do
        {
            var number = random.Next(0, 1000000);
            userCode = $"P{number:D6}";
        }
        while (await _context.Users.AnyAsync(u => u.UserCode == userCode));

        return userCode;
    }

    private static string GenerateOtp()
    {
        return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private static bool VerifyPassword(string password, string passwordHash)
    {
        var parts = passwordHash.Split('.');
        if (parts.Length != 3) return false;

        var iterations = int.Parse(parts[0]);
        var salt = Convert.FromBase64String(parts[1]);
        var hash = Convert.FromBase64String(parts[2]);

        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        var computedHash = pbkdf2.GetBytes(32);

        return CryptographicOperations.FixedTimeEquals(computedHash, hash);
    }

    public static string HashPassword(string password)
    {
        const int iterations = 100000;
        var salt = RandomNumberGenerator.GetBytes(16);

        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);

        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static void ValidateProfileFields(string displayName, string phone, string address)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new InvalidOperationException("Tên hiển thị không được để trống.");

        if (string.IsNullOrWhiteSpace(phone))
            throw new InvalidOperationException("Số điện thoại không được để trống.");

        var normalizedPhone = phone.Trim();
        if (normalizedPhone.Length < 9 || normalizedPhone.Length > 20)
            throw new InvalidOperationException("Số điện thoại không hợp lệ.");

        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException("Địa chỉ không được để trống.");
    }

    private static string ResolveFileExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        // Keep only safe extension characters (a-z, 0-9) so object keys stay valid.
        var cleaned = new string(extension.Where(char.IsLetterOrDigit).ToArray());
        return cleaned.Length > 20 ? cleaned[..20] : cleaned;
    }

    private static UserProfileDto ToProfileDto(User user) => new()
    {
        Id = user.Id,
        UserCode = user.UserCode,
        Email = user.Email,
        DisplayName = user.DisplayName,
        AvatarUrl = user.AvatarUrl,
        Phone = user.Phone,
        Address = user.Address,
        UserRole = user.UserRole.ToUpperInvariant()
    };

    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex OtpPattern();
}
