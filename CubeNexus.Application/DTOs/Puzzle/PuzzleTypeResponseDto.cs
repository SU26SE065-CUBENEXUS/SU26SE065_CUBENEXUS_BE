namespace CubeNexus.Application.DTOs.Puzzle;

public class PuzzleTypeResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int? ScrambleLength { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
