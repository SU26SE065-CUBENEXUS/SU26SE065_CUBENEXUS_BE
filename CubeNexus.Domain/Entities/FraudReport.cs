namespace CubeNexus.Domain.Entities;

public class FraudReport
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid ReportedBy { get; set; }
    public Guid AccusedUserId { get; set; }
    public string? Description { get; set; }
    public string? EvidenceUrl { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public Guid? ReviewedBy { get; set; }
    public string? VerdictCode { get; set; }
    public string? AdminNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public OnlineMatch Match { get; set; } = null!;
    public User ReportedByUser { get; set; } = null!;
    public User AccusedUser { get; set; } = null!;
    public User? ReviewedByUser { get; set; }
}
