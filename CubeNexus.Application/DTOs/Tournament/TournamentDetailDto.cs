namespace CubeNexus.Application.DTOs.Tournament;

public class TournamentDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public int? MaxParticipants { get; set; }
    public int CurrentParticipants { get; set; }
    public string? BannerUrl { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime RegistrationOpenAt { get; set; }
    public DateTime RegistrationCloseAt { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public List<EventDetailDto> Events { get; set; } = new();
}

public class EventDetailDto
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string PuzzleTypeName { get; set; } = string.Empty;
    public string PuzzleTypeCode { get; set; } = string.Empty;
    public string EventFormatCode { get; set; } = string.Empty;
    public string RegistrationStatusCode { get; set; } = "OPEN";
    public int? TimeLimitMs { get; set; }
    public int? CutoffTimeMs { get; set; }
    public int SolveCount { get; set; }
    public int TotalRounds { get; set; } = 1;
    public int? AdvanceTopN { get; set; } = 16;
    public int? SortOrder { get; set; }
    public int? MaxCapacity { get; set; }
    
    public List<MedleyPuzzleDetailDto> MedleyPuzzles { get; set; } = new();
}

public class MedleyPuzzleDetailDto
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string PuzzleTypeName { get; set; } = string.Empty;
    public string PuzzleTypeCode { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
