using System;
using System.Collections.Generic;

namespace CubeNexus.Application.DTOs.Operation;

public class RoundStartedEventDto
{
    public Guid EventId { get; set; }
    public int RoundNumber { get; set; }
    public string RoundStatus { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public List<Guid> NoShowCompetitorIds { get; set; } = new();
}
