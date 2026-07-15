using System;

namespace CubeNexus.Application.DTOs.PublicLive;

public class PublicLiveTournamentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public int EventsCount { get; set; }
    public bool IsLive { get; set; }
}
