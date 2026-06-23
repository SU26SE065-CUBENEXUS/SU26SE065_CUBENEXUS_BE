using System.Security.Claims;

namespace CubeNexus.API.Security;

public static class UserClaimsHelper
{
    private static readonly string[] UserIdClaimTypes =
    [
        "id",
        "userId",
        "sub",
        ClaimTypes.NameIdentifier
    ];

    private static readonly string[] RoleClaimTypes =
    [
        ClaimTypes.Role,
        "role",
        "roles",
        "userRole"
    ];

    public static bool TryGetUserId(ClaimsPrincipal? user, out Guid userId)
    {
        userId = Guid.Empty;
        if (user == null)
            return false;

        foreach (var claimType in UserIdClaimTypes)
        {
            var rawValue = user.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(rawValue) && Guid.TryParse(rawValue, out userId))
                return true;
        }

        return false;
    }

    public static bool IsAdminOrManager(ClaimsPrincipal? user)
    {
        if (user == null)
            return false;

        if (user.IsInRole("ADMIN") || user.IsInRole("MANAGER"))
            return true;

        var roles = RoleClaimTypes
            .SelectMany(claimType => user.FindAll(claimType))
            .Select(claim => claim.Value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return roles.Contains("ADMIN") || roles.Contains("MANAGER");
    }
}
