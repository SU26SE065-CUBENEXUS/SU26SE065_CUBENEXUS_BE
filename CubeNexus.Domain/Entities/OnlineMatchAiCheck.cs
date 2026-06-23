namespace CubeNexus.Domain.Entities;

public class OnlineMatchAiCheck
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public string CheckType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public Guid? VideoEvidenceId { get; set; }
    public string? ModelVersion { get; set; }
    public string? ResultJson { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }

    public OnlineMatch Match { get; set; } = null!;
    public User Player { get; set; } = null!;
    public OnlineMatchVideoEvidence? VideoEvidence { get; set; }
}
