namespace CubeNexus.Domain.Entities;

/// <summary>
/// Business session for enrollment/verification. ExternalSessionId maps to FastAPI session.
/// </summary>
public class FaceVerificationSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = "VERIFICATION";
    public string ContextType { get; set; } = "CHECK_IN";
    public Guid? TournamentId { get; set; }
    public Guid? RegistrationId { get; set; }
    public Guid? InitiatedByUserId { get; set; }
    public string ExternalSessionId { get; set; } = string.Empty;
    public string UploadToken { get; set; } = string.Empty;
    public string? ChallengeJson { get; set; }
    public string State { get; set; } = "POSITIONING";
    public string? ResultJson { get; set; }
    public string? FailureReason { get; set; }
    public bool? LivenessPassed { get; set; }
    public bool? FaceMatched { get; set; }
    public double? Similarity { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public User User { get; set; } = null!;
    public Tournament? Tournament { get; set; }
    public Registration? Registration { get; set; }
    public User? InitiatedByUser { get; set; }
}
