namespace CubeNexus.Domain.Entities;

public class FraudReport
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid ReporterUserId { get; set; }
    public Guid ReportedUserId { get; set; }
    public string? ReasonCode { get; set; }
    public string? Description { get; set; }
    public string? EvidenceUrl { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string ReviewScope { get; set; } = "WHOLE_MATCH";
    public string? Decision { get; set; }
    public string? PenaltyAction { get; set; }
    public Guid? ResolvedByAdminId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public string? VerdictCode { get; set; }
    public string? AdminNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public OnlineMatch Match { get; set; } = null!;
    public User ReportedByUser { get; set; } = null!;
    public User AccusedUser { get; set; } = null!;
    public User? ReviewedByUser { get; set; }
    public User? ResolvedByAdmin { get; set; }
}
