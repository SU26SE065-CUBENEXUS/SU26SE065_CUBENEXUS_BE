namespace CubeNexus.Application.DTOs.OnlineArena;

public class MatchmakingStatusDto
{
    public string Status { get; set; } = string.Empty;
    public Guid? QueueId { get; set; }
    public Guid? MatchId { get; set; }
    public string? MatchStatus { get; set; }
    public string? RoomToken { get; set; }
    public string? QrSessionCode { get; set; }
    public Guid? MeUserId { get; set; }
    public Guid? OpponentUserId { get; set; }
}
