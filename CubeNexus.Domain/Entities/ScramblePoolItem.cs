namespace CubeNexus.Domain.Entities;

public class ScramblePoolItem
{
    public Guid Id { get; set; }
    public string CompetitionMode { get; set; } = string.Empty;
    public Guid PuzzleTypeId { get; set; }
    public string Sequence { get; set; } = string.Empty;
    public string SequenceHash { get; set; } = string.Empty;
    public string? ExpectedStateJson { get; set; }
    public string Status { get; set; } = "DRAFT";
    public bool IsValidated { get; set; }
    public string GeneratorName { get; set; } = "ADMIN_IMPORT";
    public string? Notes { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? AssignedTargetType { get; set; }
    public Guid? AssignedTargetId { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public PuzzleType PuzzleType { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public User? ApprovedByUser { get; set; }
    public ICollection<ScramblePoolAuditLog> AuditLogs { get; set; } = new List<ScramblePoolAuditLog>();
}
