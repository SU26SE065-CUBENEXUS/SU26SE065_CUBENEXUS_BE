using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Repositories;

public interface IOnlineProfileRepository : IRepository<OnlineProfile>
{
    Task<OnlineProfile?> GetByUserIdAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<OnlineProfile?> GetByUserAndPuzzleTypeAsync(
        Guid userId,
        Guid puzzleTypeId,
        CancellationToken ct = default);

    Task<(List<OnlineProfile> Items, int TotalCount)> GetLeaderboardAsync(
        Guid puzzleTypeId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
