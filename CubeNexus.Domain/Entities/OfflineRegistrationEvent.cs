namespace CubeNexus.Domain.Entities;

public class OfflineRegistrationEvent
{
    public Guid Id { get; set; }
    public Guid RegistrationId { get; set; }
    public Guid EventId { get; set; }
    public string StatusCode { get; set; } = "REGISTERED";
    public int? SeedTimeMs { get; set; }
    public string? SeedSourceCode { get; set; }
    public DateTime? SeedGeneratedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Registration Registration { get; set; } = null!;
    public Event Event { get; set; } = null!;
}
