using System;
using System.Collections.Generic;

namespace CubeNexus.Application.DTOs.Registration;

public class TournamentRegistrationDetailDto
{
    public Guid RegistrationId { get; set; }
    public Guid TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string CompetitorName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string CompetitorUserCode { get; set; } = string.Empty;
    public string? CompetitorAvatarUrl { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public string QrToken { get; set; } = string.Empty;
    public List<RegisteredEventDetailDto> RegisteredEvents { get; set; } = new();
}
