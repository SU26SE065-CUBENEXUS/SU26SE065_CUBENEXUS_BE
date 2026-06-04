using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

public interface IPuzzleTypeRepository : IRepository<PuzzleType>
{
    /// <summary>Lấy theo code (không phân biệt hoa/thường)</summary>
    Task<PuzzleType?> GetByCodeAsync(string code);

    /// <summary>Kiểm tra code đã tồn tại chưa (không phân biệt hoa/thường)</summary>
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null);

    Task<IReadOnlyList<PuzzleType>> GetAllActiveAsync();

    Task<IReadOnlyList<PuzzleType>> GetAllPuzzleTypesAsync();
}
