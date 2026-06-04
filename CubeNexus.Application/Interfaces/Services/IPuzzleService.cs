using CubeNexus.Application.DTOs.Puzzle;

namespace CubeNexus.Application.Interfaces.Services;

public interface IPuzzleService
{
    /// <summary>Lấy tất cả loại puzzle</summary>
    Task<IReadOnlyList<PuzzleTypeResponseDto>> GetAllAsync();

    Task<PuzzleTypeResponseDto> GetByIdAsync(Guid id);

    /// <summary>Admin: tạo loại puzzle mới</summary>
    Task<PuzzleTypeResponseDto> CreateAsync(CreatePuzzleTypeDto dto);

    /// <summary>Admin: cập nhật thông tin puzzle</summary>
    Task<PuzzleTypeResponseDto> UpdateAsync(Guid id, UpdatePuzzleTypeDto dto);

    /// <summary>Admin: xóa mềm (đánh dấu inactive)</summary>
    Task DeactivateAsync(Guid id);

    /// <summary>Admin: khôi phục puzzle đã deactivate</summary>
    Task ActivateAsync(Guid id);
}
