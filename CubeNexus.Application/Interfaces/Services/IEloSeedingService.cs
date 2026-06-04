using CubeNexus.Application.DTOs.Arena;
using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Services;

/// <summary>
/// Dịch vụ xử lý Giai đoạn 1: Seeding từ Practice Ao5.
/// </summary>
public interface IEloSeedingService
{
    Task<PracticeStatusDto> GetPracticeStatusAsync(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default);

    Task<PracticeAo5Snapshot?> CalculateAndSaveAo5Async(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default);

    Task<OnlineProfile> InitializeOnlineProfileAsync(
        Guid userId, Guid puzzleTypeId, CancellationToken ct = default);
}
