namespace CubeNexus.Application.DTOs.Operation;

public class StartRoundResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<MissingCompetitorDto> MissingCompetitors { get; set; } = new();
}

public class MissingCompetitorDto
{
    public Guid GroupCompetitorId { get; set; }
    public string CompetitorName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int? StationNumber { get; set; }
}
