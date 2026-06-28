namespace CubeNexus.Domain.Entities;

public class OnlineMatchAuditLog
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid? PlayerId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTime CreatedAt { get; set; }

    public OnlineMatch Match { get; set; } = null!;
    public User? Player { get; set; }
}
