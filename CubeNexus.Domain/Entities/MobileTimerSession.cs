namespace CubeNexus.Domain.Entities;

public class MobileTimerSession
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid UserId { get; set; }
    public string QrSessionCode { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public bool IsActive { get; set; } = false;

    public OnlineMatch Match { get; set; } = null!;
    public User User { get; set; } = null!;
}
