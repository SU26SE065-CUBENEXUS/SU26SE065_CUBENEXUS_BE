namespace CubeNexus.Domain.Entities;

/// <summary>
/// Business mirror of a user face enrollment. Embeddings stay in FastAPI template store.
/// </summary>
public class FaceEnrollment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "ENROLLED";
    public string? ModelVersion { get; set; }
    public double? QualityScore { get; set; }
    public int TemplatesCount { get; set; }
    public string? LastExternalSessionId { get; set; }
    public DateTime EnrolledAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
