using CubeNexus.Application.Interfaces.Repositories;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Repositories;

/// <summary>
/// Repository cho EloConfig – luôn lấy bản mới nhất.
/// </summary>
public class EloConfigRepository : Repository<EloConfig>, IEloConfigRepository
{
    public EloConfigRepository(ApplicationDbContext db) : base(db) { }

    /// <inheritdoc/>
    public async Task<EloConfig> GetActiveConfigAsync(CancellationToken ct = default)
    {
        var config = await _db.EloConfigs
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (config is null)
            throw new InvalidOperationException(
                "Không tìm thấy cấu hình Elo. Admin cần khởi tạo dữ liệu bảng elo_config.");

        return config;
    }
}
