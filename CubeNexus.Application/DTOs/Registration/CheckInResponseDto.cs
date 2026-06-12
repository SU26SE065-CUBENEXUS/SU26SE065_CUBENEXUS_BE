namespace CubeNexus.Application.DTOs.Registration;

public class CheckInResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool AlreadyCheckedIn { get; set; }
    public Guid RegistrationId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string TournamentName { get; set; } = string.Empty;
    public DateTime? CheckedInAt { get; set; }
    public List<string> Events { get; set; } = new();
}
