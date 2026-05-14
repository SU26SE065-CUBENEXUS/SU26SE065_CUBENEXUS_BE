namespace CubeNexus.Domain.Entities;

public class MatchmakingQueue
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid OnlineProfileId { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public DateTime QueuedAt { get; set; }
    public string StatusCode { get; set; } = string.Empty;

    public User User { get; set; } = null!;
    public OnlineProfile OnlineProfile { get; set; } = null!;
    public PuzzleType PuzzleType { get; set; } = null!;
}
