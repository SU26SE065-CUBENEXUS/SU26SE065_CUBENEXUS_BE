using CubeNexus.Application.DTOs.Admin;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Domain.Entities;
using CubeNexus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeNexus.Infrastructure.Services;

public class AdminUserService : IAdminUserService
{
    private readonly ApplicationDbContext _context;

    public AdminUserService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AdminUserPagedResultDto> GetUsersAsync(
        int page = 1,
        int pageSize = 10,
        string? search = null,
        string? role = null,
        string? status = null,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : (pageSize > 100 ? 100 : pageSize);

        var query = _context.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(s) ||
                u.Email.ToLower().Contains(s) ||
                u.UserCode.ToLower().Contains(s) ||
                u.Phone.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(role) && !string.Equals(role, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(u => u.UserRole.ToUpper() == role.Trim().ToUpper());
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var st = status.Trim().ToLower();
            if (st == "banned")
            {
                query = query.Where(u => u.IsBanned);
            }
            else if (st == "active")
            {
                query = query.Where(u => u.IsActive && !u.IsBanned);
            }
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                UserCode = u.UserCode,
                Email = u.Email,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                Phone = u.Phone,
                Address = u.Address,
                UserRole = u.UserRole,
                IsActive = u.IsActive,
                IsBanned = u.IsBanned,
                BanReason = u.BanReason,
                BannedAt = u.BannedAt,
                BannedUntil = u.BannedUntil,
                EmailConfirmed = u.EmailConfirmed,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
            })
            .ToListAsync(ct);

        return new AdminUserPagedResultDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<AdminUserDto> GetUserByIdAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u == null)
        {
            throw new KeyNotFoundException("Không tìm thấy người dùng.");
        }

        return new AdminUserDto
        {
            Id = u.Id,
            UserCode = u.UserCode,
            Email = u.Email,
            DisplayName = u.DisplayName,
            AvatarUrl = u.AvatarUrl,
            Phone = u.Phone,
            Address = u.Address,
            UserRole = u.UserRole,
            IsActive = u.IsActive,
            IsBanned = u.IsBanned,
            BanReason = u.BanReason,
            BannedAt = u.BannedAt,
            BannedUntil = u.BannedUntil,
            EmailConfirmed = u.EmailConfirmed,
            CreatedAt = u.CreatedAt,
            UpdatedAt = u.UpdatedAt,
        };
    }

    public async Task<AdminUserDto> UpdateUserRoleAsync(Guid currentAdminId, Guid targetUserId, string newRole, CancellationToken ct = default)
    {
        var validRoles = new[] { "ADMIN", "MANAGER", "JUDGE", "COMPETITOR" };
        var normalizedRole = (newRole ?? "").Trim().ToUpper();

        if (!validRoles.Contains(normalizedRole))
        {
            throw new ArgumentException($"Role '{newRole}' không hợp lệ. Các role cho phép: ADMIN, MANAGER, JUDGE, COMPETITOR.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy người dùng cần đổi role.");
        }

        if (currentAdminId == targetUserId && normalizedRole != "ADMIN")
        {
            throw new InvalidOperationException("Bạn không thể tự hạ cấp vai trò ADMIN của chính mình.");
        }

        user.UserRole = normalizedRole;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return new AdminUserDto
        {
            Id = user.Id,
            UserCode = user.UserCode,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Phone = user.Phone,
            Address = user.Address,
            UserRole = user.UserRole,
            IsActive = user.IsActive,
            IsBanned = user.IsBanned,
            BanReason = user.BanReason,
            BannedAt = user.BannedAt,
            BannedUntil = user.BannedUntil,
            EmailConfirmed = user.EmailConfirmed,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
        };
    }

    public async Task<AdminUserDto> BanUserAsync(Guid currentAdminId, Guid targetUserId, BanUserRequestDto req, CancellationToken ct = default)
    {
        if (currentAdminId == targetUserId)
        {
            throw new InvalidOperationException("Bạn không thể tự cấm tài khoản của chính mình.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy người dùng cần cấm.");
        }

        if (string.IsNullOrWhiteSpace(req.BanReason))
        {
            throw new ArgumentException("Vui lòng nhập lý do cấm tài khoản.");
        }

        user.IsBanned = true;
        user.BanReason = req.BanReason.Trim();
        user.BannedAt = DateTime.UtcNow;

        if (req.DurationDays.HasValue && req.DurationDays.Value > 0)
        {
            user.BannedUntil = DateTime.UtcNow.AddDays(req.DurationDays.Value);
        }
        else
        {
            user.BannedUntil = null; // Permanent ban
        }

        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return new AdminUserDto
        {
            Id = user.Id,
            UserCode = user.UserCode,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Phone = user.Phone,
            Address = user.Address,
            UserRole = user.UserRole,
            IsActive = user.IsActive,
            IsBanned = user.IsBanned,
            BanReason = user.BanReason,
            BannedAt = user.BannedAt,
            BannedUntil = user.BannedUntil,
            EmailConfirmed = user.EmailConfirmed,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
        };
    }

    public async Task<AdminUserDto> UnbanUserAsync(Guid targetUserId, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy người dùng cần gỡ cấm.");
        }

        user.IsBanned = false;
        user.BanReason = null;
        user.BannedAt = null;
        user.BannedUntil = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return new AdminUserDto
        {
            Id = user.Id,
            UserCode = user.UserCode,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Phone = user.Phone,
            Address = user.Address,
            UserRole = user.UserRole,
            IsActive = user.IsActive,
            IsBanned = user.IsBanned,
            BanReason = user.BanReason,
            BannedAt = user.BannedAt,
            BannedUntil = user.BannedUntil,
            EmailConfirmed = user.EmailConfirmed,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
        };
    }
}
