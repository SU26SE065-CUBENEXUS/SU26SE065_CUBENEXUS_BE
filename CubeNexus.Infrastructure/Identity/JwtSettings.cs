namespace CubeNexus.Infrastructure.Identity;

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpirationInMinutes { get; set; } = 30;
    public int RefreshTokenExpirationInDays { get; set; } = 7;
}
