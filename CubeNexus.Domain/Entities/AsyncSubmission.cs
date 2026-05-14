namespace CubeNexus.Domain.Entities;

public class AsyncSubmission
{
    public Guid Id { get; set; }
    public Guid AsyncTournamentId { get; set; }
    public Guid UserId { get; set; }
    public string VideoUrl { get; set; } = string.Empty;
    public int ClaimedTimeMs { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public Guid? ReviewedBy { get; set; }
    public string? AdminNote { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public AsyncTournament AsyncTournament { get; set; } = null!;
    public User User { get; set; } = null!;
    public User? ReviewedByUser { get; set; }
}
