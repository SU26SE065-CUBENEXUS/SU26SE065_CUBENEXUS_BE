namespace CubeNexus.Domain.Entities;

public class Dispute
{
    public Guid Id { get; set; }
    public Guid ResultId { get; set; }
    public Guid ReportedBy { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public Result Result { get; set; } = null!;
    public User ReportedByUser { get; set; } = null!;
    public User? ResolvedByUser { get; set; }
}
