using System;

namespace CubeNexus.Application.DTOs.Registration;

public class OverrideSeedDto
{
    public int? SeedTimeMs { get; set; }
}

public class EventCompetitorSeedDto
{
    public Guid RegistrationEventId { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int? SeedTimeMs { get; set; }
    public string? SeedSourceCode { get; set; }
    public DateTime? SeedGeneratedAt { get; set; }
}
