using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

public interface IOnlineProfileRepository : IRepository<OnlineProfile>
{
    Task<OnlineProfile?> GetByUserIdAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<(List<OnlineProfile> Items, int TotalCount)> GetLeaderboardAsync(
        int page,
        int pageSize,
        CancellationToken ct = default);
}
