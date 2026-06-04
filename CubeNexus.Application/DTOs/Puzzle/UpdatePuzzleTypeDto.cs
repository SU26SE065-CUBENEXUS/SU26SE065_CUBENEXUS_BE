namespace CubeNexus.Application.DTOs.Puzzle;

public class UpdatePuzzleTypeDto
{
    public string Name { get; set; } = string.Empty;
    public int? ScrambleLength { get; set; }
    public bool IsActive { get; set; } = true;
}
