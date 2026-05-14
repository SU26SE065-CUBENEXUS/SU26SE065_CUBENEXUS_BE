namespace CubeNexus.Domain.Entities;

public class GroupCompetitor
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid RegistrationId { get; set; }
    public int? SeedTimeMs { get; set; }
    public int? StationNumber { get; set; }

    public Group Group { get; set; } = null!;
    public Registration Registration { get; set; } = null!;
}
