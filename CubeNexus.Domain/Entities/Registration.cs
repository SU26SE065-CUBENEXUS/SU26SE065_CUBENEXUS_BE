namespace CubeNexus.Domain.Entities;

public class Registration
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public Guid UserId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string QrToken { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public DateTime? CheckedInAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
    public User User { get; set; } = null!;
    public ICollection<OfflineRegistrationEvent> OfflineRegistrationEvents { get; set; } = new List<OfflineRegistrationEvent>();
}
