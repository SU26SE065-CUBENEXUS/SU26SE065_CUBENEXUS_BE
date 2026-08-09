namespace CubeNexus.Application.DTOs.Admin;

public class UpdateUserRoleRequestDto
{
    public string UserRole { get; set; } = string.Empty;
}

public class BanUserRequestDto
{
    /// <summary>
    /// Duration in days (e.g. 1, 7, 30, 90). If null or 0, considered Permanent.
    /// </summary>
    public int? DurationDays { get; set; }
    public string BanReason { get; set; } = string.Empty;
}

public class AdminUserPagedResultDto
{
    public List<AdminUserDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
