namespace CubeNexus.Application.DTOs.OnlineArena;

public class CreateMatchRecordingUploadUrlRequest
{
    public string ContentType { get; set; } = string.Empty;
    public string? FileExtension { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTime? RecordedAt { get; set; }
}

public class CompleteMatchRecordingUploadRequest
{
    public string ObjectKey { get; set; } = string.Empty;
    public double? DurationSeconds { get; set; }
}

public class MatchRecordingUploadUrlResponseDto
{
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid VideoEvidenceId { get; set; }
    public string RecordingStatus { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string UploadUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public Dictionary<string, string> RequiredHeaders { get; set; } = [];
}

public class MatchRecordingCompleteResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid VideoEvidenceId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public double? DurationSeconds { get; set; }
    public string RecordingStatus { get; set; } = string.Empty;
    public DateTime? RecordedAt { get; set; }
    public DateTime? UploadedAt { get; set; }
}

public class MatchRecordingPlaybackItemDto
{
    public Guid VideoEvidenceId { get; set; }
    public Guid PlayerId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public double? DurationSeconds { get; set; }
    public string RecordingStatus { get; set; } = string.Empty;
    public DateTime? RecordedAt { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string PlaybackUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class MatchRecordingPlaybackResponseDto
{
    public Guid MatchId { get; set; }
    public List<MatchRecordingPlaybackItemDto> Recordings { get; set; } = [];
}
