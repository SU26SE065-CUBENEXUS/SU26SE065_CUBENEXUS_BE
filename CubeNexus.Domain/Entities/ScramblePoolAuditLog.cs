namespace CubeNexus.Domain.Entities;

public class ScramblePoolAuditLog
{
    public Guid Id { get; set; }
    public Guid ScramblePoolItemId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public string? DetailsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ScramblePoolItem ScramblePoolItem { get; set; } = null!;
    public User? ActorUser { get; set; }
}
