namespace CubeNexus.Domain.Entities;

public class ScrambleSet
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string? PdfUrl { get; set; }
    public string? PdfPasswordHash { get; set; }
    public DateTime GeneratedAt { get; set; }
    public Guid? GeneratedBy { get; set; }

    public Group Group { get; set; } = null!;
    public User? GeneratedByUser { get; set; }
}
