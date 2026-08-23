namespace CubeNexus.Application.DTOs.Registration;

public sealed class DemoParticipantGenerationResultDto
{
    public Guid TournamentId { get; set; }
    public int RequestedCount { get; set; }
    public int NewRegistrations { get; set; }
    public int ExistingRegistrations { get; set; }
    public List<string> ParticipantCodes { get; set; } = [];
}
