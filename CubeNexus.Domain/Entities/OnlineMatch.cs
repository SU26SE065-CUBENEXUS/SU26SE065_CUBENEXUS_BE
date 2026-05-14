namespace CubeNexus.Domain.Entities;

public class OnlineMatch
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string ScrambleSequence { get; set; } = string.Empty;
    public Guid Player1Id { get; set; }
    public Guid Player2Id { get; set; }
    public Guid? WinnerId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string RoomToken { get; set; } = string.Empty;
    public string? QrSessionCode { get; set; }
    public int? Player1TimeMs { get; set; }
    public int? Player2TimeMs { get; set; }
    public int? Player1EloBefore { get; set; }
    public int? Player2EloBefore { get; set; }
    public int? Player1EloAfter { get; set; }
    public int? Player2EloAfter { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public PuzzleType PuzzleType { get; set; } = null!;
    public User Player1 { get; set; } = null!;
    public User Player2 { get; set; } = null!;
    public User? Winner { get; set; }
}
