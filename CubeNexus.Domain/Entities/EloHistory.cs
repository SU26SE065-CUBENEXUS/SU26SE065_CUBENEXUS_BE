namespace CubeNexus.Domain.Entities;

public class EloHistory
{
    public Guid Id { get; set; }
    public Guid OnlineProfileId { get; set; }
    public Guid? MatchId { get; set; }
    public int EloBefore { get; set; }
    public int EloAfter { get; set; }
    public int Delta { get; set; }
    public string? ReasonCode { get; set; }
    public DateTime ChangedAt { get; set; }

    public OnlineProfile OnlineProfile { get; set; } = null!;
    public OnlineMatch? Match { get; set; }
}
