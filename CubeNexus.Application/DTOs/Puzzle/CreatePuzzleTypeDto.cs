namespace CubeNexus.Application.DTOs.Puzzle;

public class CreatePuzzleTypeDto
{
    /// <summary>Tên loại rubik, ví dụ: "Rubik 3x3", "Pyraminx"</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Mã định danh ngắn, ví dụ: "333", "222", "PYRA"</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Số bước tráo chuẩn (có thể null nếu chưa xác định)</summary>
    public int? ScrambleLength { get; set; }
}
