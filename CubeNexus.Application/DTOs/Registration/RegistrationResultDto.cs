namespace CubeNexus.Application.DTOs.Registration;

public class RegistrationResultDto
{
    public Guid RegistrationId { get; set; }
    public Guid TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    
    // Serialized JSON of RegistrationQrPayload or similar representation
    public string QrToken { get; set; } = string.Empty;

    public List<RegisteredEventDetailDto> RegisteredEvents { get; set; } = new();
}

public class RegisteredEventDetailDto
{
    public Guid RegistrationEventId { get; set; }
    public Guid EventId { get; set; }
    public string PuzzleTypeName { get; set; } = string.Empty;
    public string EventFormatCode { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public int? SeedTimeMs { get; set; }
    public string? SeedSourceCode { get; set; }
    public DateTime? SeedGeneratedAt { get; set; }
}

public class RegistrationQrPayload
{
    public Guid RegistrationId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
