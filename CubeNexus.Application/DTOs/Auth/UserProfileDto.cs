namespace CubeNexus.Application.DTOs.Auth;

public class UserProfileDto
{
    public Guid Id { get; set; }
    public string UserCode { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
}
