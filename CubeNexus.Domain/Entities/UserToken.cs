namespace CubeNexus.Domain.Entities;

public class UserToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenType { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsUsed => UsedAt != null;
    public bool IsActive => !IsUsed && !IsExpired;

    public User User { get; set; } = null!;
}
