using System.ComponentModel.DataAnnotations;

namespace CubeNexus.Application.DTOs.Registration;

public class RegisterTournamentDto
{
    [Required]
    public List<RegisterEventDto> Events { get; set; } = new();
}

public class RegisterEventDto
{
    [Required]
    public Guid EventId { get; set; }
}
