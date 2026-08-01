namespace CubeNexus.Domain.Entities;

public class Result
{
    public Guid Id { get; set; }
    public Guid GroupCompetitorId { get; set; }
    public Guid? ScrambleId { get; set; }
    public Guid JudgedBy { get; set; }
    public int SolveNumber { get; set; }
    public int? RawTimeMs { get; set; }
    public int? FinalTimeMs { get; set; }
    public Guid? PenaltyTypeId { get; set; }
    public bool IsDnf { get; set; } = false;
    public string? EsignatureData { get; set; }
    public string? EvidencePhotoUrl { get; set; }
    public DateTime? SignedAt { get; set; }
    public DateTime SubmittedAt { get; set; }
    public bool IsLocked { get; set; } = false;

    public GroupCompetitor GroupCompetitor { get; set; } = null!;
    public Scramble? Scramble { get; set; }
    public User JudgedByUser { get; set; } = null!;
    public PenaltyType? PenaltyType { get; set; }
}
