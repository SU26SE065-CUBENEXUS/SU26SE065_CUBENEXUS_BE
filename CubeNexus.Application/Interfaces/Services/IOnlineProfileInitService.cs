using CubeNexus.Domain.Entities;

namespace CubeNexus.Application.Interfaces.Services;

public interface IOnlineProfileInitService
{
    /// <summary>
    /// Tạo online profile với elo_standard mặc định nếu user chưa có.
    /// Được gọi khi đăng ký — user có thể vào PVP ngay sau đó.
    /// </summary>
    Task<OnlineProfile> EnsureStandardProfileAsync(Guid userId, CancellationToken ct = default);
}
