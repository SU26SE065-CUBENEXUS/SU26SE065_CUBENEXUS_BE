using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

public class PuzzleTypeRepository : Repository<PuzzleType>, IPuzzleTypeRepository
{
    private readonly ApplicationDbContext _context;

    public PuzzleTypeRepository(ApplicationDbContext db) : base(db)
    {
        _context = db;
    }

    public async Task<PuzzleType?> GetByCodeAsync(string code)
        => await _context.PuzzleTypes
            .FirstOrDefaultAsync(p => p.Code.ToLower() == code.ToLower());

    public async Task<bool> CodeExistsAsync(string code, Guid? excludeId = null)
        => await _context.PuzzleTypes
            .AnyAsync(p =>
                p.Code.ToLower() == code.ToLower() &&
                (excludeId == null || p.Id != excludeId));

    public async Task<IReadOnlyList<PuzzleType>> GetAllActiveAsync()
        => await _context.PuzzleTypes
            .Where(p => p.IsActive)
            .OrderBy(p => p.Code)
            .ToListAsync();

    public async Task<IReadOnlyList<PuzzleType>> GetAllPuzzleTypesAsync()
        => await _context.PuzzleTypes
            .OrderBy(p => p.Code)
            .ToListAsync();
}
