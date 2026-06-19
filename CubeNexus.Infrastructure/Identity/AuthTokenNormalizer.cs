namespace CubeNexus.Infrastructure.Identity;

public static class AuthTokenNormalizer
{
    public static string NormalizeOtp(string otp)
    {
        if (string.IsNullOrWhiteSpace(otp))
            return string.Empty;

        return otp.Trim();
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
