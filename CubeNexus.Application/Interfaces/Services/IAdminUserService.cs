using CubeNexus.Application.DTOs.Admin;

namespace CubeNexus.Application.Interfaces.Services;

public interface IAdminUserService
{
    Task<AdminUserPagedResultDto> GetUsersAsync(int page, int pageSize, string? search, string? role, string? status, CancellationToken ct = default);
    Task<AdminUserDto> GetUserByIdAsync(Guid userId, CancellationToken ct = default);
    Task<AdminUserDto> UpdateUserRoleAsync(Guid currentAdminId, Guid targetUserId, string newRole, CancellationToken ct = default);
    Task<AdminUserDto> BanUserAsync(Guid currentAdminId, Guid targetUserId, BanUserRequestDto req, CancellationToken ct = default);
    Task<AdminUserDto> UnbanUserAsync(Guid targetUserId, CancellationToken ct = default);
}
