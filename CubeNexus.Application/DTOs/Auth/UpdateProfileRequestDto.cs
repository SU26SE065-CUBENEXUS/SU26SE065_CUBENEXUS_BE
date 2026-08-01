namespace CubeNexus.Application.DTOs.Auth;

/// <summary>
/// Partial profile update. Only non-null fields are applied.
/// </summary>
public class UpdateProfileRequestDto
{
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? AvatarUrl { get; set; }
}
