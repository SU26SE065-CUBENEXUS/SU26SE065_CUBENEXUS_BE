using System.ComponentModel.DataAnnotations;

namespace CubeNexus.Application.DTOs.Tournament;

public class CreateTournamentDto
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public int? MaxParticipants { get; set; }
    public string? BannerUrl { get; set; }
    public string? BannerPhotoData { get; set; }
    
    [Required]
    public DateTime StartDate { get; set; }
    
    [Required]
    public DateTime EndDate { get; set; }
    
    [Required]
    public DateTime RegistrationOpenAt { get; set; }
    
    [Required]
    public DateTime RegistrationCloseAt { get; set; }

    [Required]
    public List<CreateEventDto> Events { get; set; } = new();
}

public class CreateEventDto
{
    [Required]
    public Guid PuzzleTypeId { get; set; }
    
    [Required]
    public string EventFormatCode { get; set; } = "TRADITIONAL";
    
    public int? TimeLimitMs { get; set; }
    public int? CutoffTimeMs { get; set; }
    
    public int SolveCount { get; set; } = 5;
    public int TotalRounds { get; set; } = 1;
    public int? AdvanceTopN { get; set; } = 16;
    public int? SortOrder { get; set; }
    public int? MaxCapacity { get; set; }

    public List<CreateMedleyPuzzleDto> MedleyPuzzles { get; set; } = new();
}

public class CreateMedleyPuzzleDto
{
    [Required]
    public Guid PuzzleTypeId { get; set; }
    
    [Required]
    public int SortOrder { get; set; }
}
