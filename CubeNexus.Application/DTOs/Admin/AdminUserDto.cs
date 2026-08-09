namespace CubeNexus.Application.DTOs.Admin;

public class AdminUserDto
{
    public Guid Id { get; set; }
    public string UserCode { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string UserRole { get; set; } = "COMPETITOR";
    public bool IsActive { get; set; } = true;
    public bool IsBanned { get; set; } = false;
    public string? BanReason { get; set; }
    public DateTime? BannedAt { get; set; }
    public DateTime? BannedUntil { get; set; }
    public bool EmailConfirmed { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
