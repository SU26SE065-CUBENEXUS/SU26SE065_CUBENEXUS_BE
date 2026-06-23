namespace CubeNexus.Application.DTOs.OnlineArena;

public class OnlineMatchDto
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public Guid Player1Id { get; set; }
    public Guid Player2Id { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string RoomToken { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
