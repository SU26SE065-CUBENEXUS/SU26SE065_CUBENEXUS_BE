namespace CubeNexus.Application.DTOs.Practice;

/// <summary>Bắt đầu session tập luyện</summary>
public class StartPracticeSessionDto
{
    /// <summary>ID loại rubik muốn tập</summary>
    public Guid PuzzleTypeId { get; set; }
}
