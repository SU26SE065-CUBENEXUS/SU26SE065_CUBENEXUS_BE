namespace CubeNexus.Domain.Entities;

public class TournamentJudge
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public Guid UserId { get; set; }
    public string RoleCode { get; set; } = "STATION_JUDGE";
    public int? AssignedStationNumber { get; set; }
    public DateTime AssignedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
    public User User { get; set; } = null!;
}
