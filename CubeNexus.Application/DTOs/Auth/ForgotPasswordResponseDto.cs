using System.Text.Json.Serialization;

namespace CubeNexus.Application.DTOs.Auth;

public class ForgotPasswordResponseDto
{
    public string Message { get; set; } = string.Empty;

    /// <summary>Chỉ có khi chạy Development — dùng test khi email tạm nhận chậm.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DevOtp { get; set; }

    /// <summary>Chỉ có khi chạy Development — false nếu SMTP chưa cấu hình (preview mode).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EmailSent { get; set; }
}
