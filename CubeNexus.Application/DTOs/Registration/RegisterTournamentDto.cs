using System.ComponentModel.DataAnnotations;

namespace CubeNexus.Application.DTOs.Registration;

public class RegisterTournamentDto
{
    public List<RegisterEventDto> Events { get; set; } = new();

    public List<Guid>? SelectedEventIds { get; set; }
}

public class RegisterEventDto
{
    [Required]
    public Guid EventId { get; set; }
}
