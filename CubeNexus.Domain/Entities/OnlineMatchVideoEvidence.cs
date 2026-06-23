namespace CubeNexus.Domain.Entities;

public class OnlineMatchVideoEvidence
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public string? ObjectKey { get; set; }
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public double? DurationSeconds { get; set; }
    public string RecordingStatus { get; set; } = "Pending";
    public DateTime? RecordedAt { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public long? DurationMs { get; set; }
    public DateTime? RecordingStartedAt { get; set; }
    public DateTime? RecordingEndedAt { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Checksum { get; set; }
    public string SourceType { get; set; } = "LOCAL_CAMERA";
    public string? MimeType { get; set; }

    public OnlineMatch Match { get; set; } = null!;
    public User Player { get; set; } = null!;
}
