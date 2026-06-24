namespace CubeNexus.Application.DTOs.Auth;

public class LogoutRequestDto
{
    /// <summary>
    /// Refresh token của phiên hiện tại. Nếu bỏ trống, thu hồi tất cả refresh token đang hoạt động của user.
    /// </summary>
    public string? RefreshToken { get; set; }
}
