namespace CubeNexus.Application.DTOs.Admin;

public sealed record ScramblePoolItemDto(Guid Id, string CompetitionMode, Guid PuzzleTypeId,
    string PuzzleCode, string PuzzleName, string Sequence, string Status, bool IsValidated,
    string GeneratorName, string? Notes, DateTime CreatedAt, DateTime? ApprovedAt,
    string? AssignedTargetType, Guid? AssignedTargetId, DateTime? AssignedAt, int? QueuePosition);

public sealed record ScramblePoolSummaryDto(string CompetitionMode, Guid PuzzleTypeId,
    string PuzzleCode, string Status, int Count);

public sealed record ScramblePoolPageDto(IReadOnlyList<ScramblePoolItemDto> Items,
    int Total, int Page, int PageSize);

public sealed class GenerateScramblesRequestDto
{
    public string CompetitionMode { get; set; } = string.Empty;
    public Guid PuzzleTypeId { get; set; }
    public int Count { get; set; }
    public string? Notes { get; set; }
    public bool AutoApprove { get; set; }
}

public sealed class ImportScramblesRequestDto
{
    public string CompetitionMode { get; set; } = string.Empty;
    public Guid PuzzleTypeId { get; set; }
    public List<string> Sequences { get; set; } = [];
    public string? Notes { get; set; }
}

public sealed record ScrambleReservationDto(Guid Id, string Sequence, string? ExpectedStateJson);
