namespace CubeNexus.Application.DTOs.Practice;

public class PracticeAttemptResponseDto
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ScrambleSequence { get; set; } = string.Empty;

    public DateTime? HandsOnAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }

    public IReadOnlyList<string> AllowedActions { get; set; } = [];

    public Guid? SolveId { get; set; }
    public int? TimeMs { get; set; }
    public string? PenaltyCode { get; set; }
    public int? DisplayTimeMs { get; set; }
    public int? CurrentAo5Ms { get; set; }
    public string? AbortReason { get; set; }
}
