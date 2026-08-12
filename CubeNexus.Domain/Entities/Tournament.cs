namespace CubeNexus.Domain.Entities;

public class Tournament
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public int? MaxParticipants { get; set; }
    public string? BannerUrl { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime RegistrationOpenAt { get; set; }
    public DateTime RegistrationCloseAt { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string TournamentType { get; set; } = "OFFLINE"; // OFFLINE | ONLINE_ASYNC
    public string FormatCode { get; set; } = "AO1"; // AO1
    public Guid? PuzzleTypeId { get; set; }
    public string? ScrambleSequence { get; set; }
    public int AttemptTimeLimitMs { get; set; } = 300000; // 5 minutes default

    public User CreatedByUser { get; set; } = null!;
    public PuzzleType? PuzzleType { get; set; }
    public ICollection<Event> Events { get; set; } = new List<Event>();
    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
    public ICollection<OnlineAsyncAttempt> OnlineAsyncAttempts { get; set; } = new List<OnlineAsyncAttempt>();
}
