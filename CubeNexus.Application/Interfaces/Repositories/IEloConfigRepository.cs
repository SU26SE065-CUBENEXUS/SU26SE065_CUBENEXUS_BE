using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

/// <summary>
/// Repository cho EloConfig – cấu hình do Admin quản lý.
/// </summary>
public interface IEloConfigRepository : IRepository<EloConfig>
{
    /// <summary>
    /// Lấy cấu hình Elo đang hoạt động (bản mới nhất theo updated_at).
    /// Throw nếu chưa có dữ liệu seed.
    /// </summary>
    Task<EloConfig> GetActiveConfigAsync(CancellationToken ct = default);
}
