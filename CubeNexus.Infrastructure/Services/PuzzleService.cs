using CubeNexus.Application.DTOs.Puzzle;
using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Infrastructure.Services;

public class PuzzleService : IPuzzleService
{
    private readonly IUnitOfWork _uow;

    public PuzzleService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<IReadOnlyList<PuzzleTypeResponseDto>> GetAllAsync()
    {
        var list = await _uow.PuzzleTypes.GetAllPuzzleTypesAsync();
        return list.Select(MapToDto).ToList();
    }

    public async Task<PuzzleTypeResponseDto> GetByIdAsync(Guid id)
    {
        var entity = await _uow.PuzzleTypes.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy puzzle type với id = {id}");

        return MapToDto(entity);
    }

    public async Task<PuzzleTypeResponseDto> CreateAsync(CreatePuzzleTypeDto dto)
    {
        // Chuẩn hoá code về UPPERCASE để thống nhất
        var normalizedCode = dto.Code.Trim().ToUpperInvariant();

        if (await _uow.PuzzleTypes.CodeExistsAsync(normalizedCode))
            throw new InvalidOperationException($"Code '{normalizedCode}' đã tồn tại.");

        var entity = new PuzzleType
        {
            Id             = Guid.NewGuid(),
            Name           = dto.Name.Trim(),
            Code           = normalizedCode,
            ScrambleLength = dto.ScrambleLength,
            IsActive       = true,
            CreatedAt      = DateTime.UtcNow
        };

        _uow.PuzzleTypes.Add(entity);
        await _uow.SaveChangesAsync();

        return MapToDto(entity);
    }

    public async Task<PuzzleTypeResponseDto> UpdateAsync(Guid id, UpdatePuzzleTypeDto dto)
    {
        var entity = await _uow.PuzzleTypes.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy puzzle type với id = {id}");

        entity.Name           = dto.Name.Trim();
        entity.ScrambleLength = dto.ScrambleLength;
        entity.IsActive       = dto.IsActive;

        _uow.PuzzleTypes.Update(entity);
        await _uow.SaveChangesAsync();

        return MapToDto(entity);
    }

    public async Task DeactivateAsync(Guid id)
    {
        var entity = await _uow.PuzzleTypes.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy puzzle type với id = {id}");

        if (!entity.IsActive)
            throw new InvalidOperationException("Puzzle type này đã ở trạng thái inactive.");

        entity.IsActive = false;
        _uow.PuzzleTypes.Update(entity);
        await _uow.SaveChangesAsync();
    }

    public async Task ActivateAsync(Guid id)
    {
        var entity = await _uow.PuzzleTypes.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy puzzle type với id = {id}");

        if (entity.IsActive)
            throw new InvalidOperationException("Puzzle type này đã ở trạng thái active.");

        entity.IsActive = true;
        _uow.PuzzleTypes.Update(entity);
        await _uow.SaveChangesAsync();
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    private static PuzzleTypeResponseDto MapToDto(PuzzleType p) => new()
    {
        Id             = p.Id,
        Name           = p.Name,
        Code           = p.Code,
        ScrambleLength = p.ScrambleLength,
        IsActive       = p.IsActive,
        CreatedAt      = p.CreatedAt
    };
}
