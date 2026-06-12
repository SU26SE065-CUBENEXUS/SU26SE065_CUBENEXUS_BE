using System;

namespace CubeNexus.Domain.Entities;

public class ResultAuditLog
{
    public Guid Id { get; set; }
    public Guid ResultId { get; set; }
    public Guid ChangedBy { get; set; }
    public int? OldRawTimeMs { get; set; }
    public int? NewRawTimeMs { get; set; }
    public int? OldFinalTimeMs { get; set; }
    public int? NewFinalTimeMs { get; set; }
    public Guid? OldPenaltyTypeId { get; set; }
    public Guid? NewPenaltyTypeId { get; set; }
    public bool OldIsDnf { get; set; }
    public bool NewIsDnf { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }

    public Result Result { get; set; } = null!;
    public User ChangedByUser { get; set; } = null!;
    public PenaltyType? OldPenaltyType { get; set; }
    public PenaltyType? NewPenaltyType { get; set; }
}
