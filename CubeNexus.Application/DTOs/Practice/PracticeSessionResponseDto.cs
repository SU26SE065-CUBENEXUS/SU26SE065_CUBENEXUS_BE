namespace CubeNexus.Application.DTOs.Practice;

public class PracticeSessionResponseDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string PuzzleTypeName { get; set; } = string.Empty;
    public string PuzzleTypeCode { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int TotalSolves { get; set; }
}
