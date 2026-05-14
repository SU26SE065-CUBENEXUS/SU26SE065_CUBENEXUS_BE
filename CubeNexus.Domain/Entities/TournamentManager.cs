namespace CubeNexus.Domain.Entities;

public class TournamentManager
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public Guid UserId { get; set; }
    public DateTime AssignedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
    public User User { get; set; } = null!;
}
