namespace CubeNexus.Domain.Entities;

public class UserRole
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? GrantedBy { get; set; }
    public DateTime GrantedAt { get; set; }

    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
    public User? GrantedByUser { get; set; }
}
