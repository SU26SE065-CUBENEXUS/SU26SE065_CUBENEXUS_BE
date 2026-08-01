namespace CubeNexus.Application.DTOs.Auth;

public class RegisterResponseDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}
