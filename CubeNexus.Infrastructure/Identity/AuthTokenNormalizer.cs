namespace CubeNexus.Infrastructure.Identity;

public static class AuthTokenNormalizer
{
    public static string NormalizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        var normalized = token.Trim();

        if (normalized.Contains('%', StringComparison.Ordinal))
            normalized = Uri.UnescapeDataString(normalized);

        // Base64 trong query string đôi khi bị đổi '+' thành khoảng trắng
        return normalized.Replace(' ', '+');
    }

    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        var normalized = email.Trim();

        if (normalized.Contains('%', StringComparison.Ordinal))
            normalized = Uri.UnescapeDataString(normalized);

        return normalized;
    }
}
