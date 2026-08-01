namespace CubeNexus.Application.DTOs.Auth;

public class LoginResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public Guid? AssignedTournamentId { get; set; }
    public string? JudgeRoleCode { get; set; }
    public int? AssignedStationNumber { get; set; }
}
