namespace CubeNexus.Domain.Entities;

public class Group
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public int RoundNumber { get; set; }
    public string? GroupName { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Event Event { get; set; } = null!;
}
