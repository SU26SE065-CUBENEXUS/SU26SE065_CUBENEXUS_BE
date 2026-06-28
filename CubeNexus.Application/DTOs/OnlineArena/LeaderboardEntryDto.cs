namespace CubeNexus.Application.DTOs.OnlineArena;

public class LeaderboardEntryDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int Rank { get; set; }
    public int Elo { get; set; }
    public int TotalWins { get; set; }
}
