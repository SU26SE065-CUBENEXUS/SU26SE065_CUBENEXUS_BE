namespace CubeNexus.Application.DTOs.Admin;

public class AdminMedleyPuzzleDto
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string PuzzleTypeName { get; set; } = string.Empty;
    public string PuzzleTypeCode { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class AdminTournamentEventDto
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string PuzzleTypeName { get; set; } = string.Empty;
    public string PuzzleTypeCode { get; set; } = string.Empty;
    public string EventFormatCode { get; set; } = "TRADITIONAL";
    public string? RegistrationStatusCode { get; set; }
    public List<AdminMedleyPuzzleDto> MedleyPuzzles { get; set; } = new();
}

public class AdminTournamentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public int? MaxParticipants { get; set; }
    public int RegisteredParticipantsCount { get; set; }
    public string? BannerUrl { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime RegistrationOpenAt { get; set; }
    public DateTime RegistrationCloseAt { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string TournamentType { get; set; } = "OFFLINE";
    public string FormatCode { get; set; } = "AO1";
    public Guid? PuzzleTypeId { get; set; }
    public string? PuzzleTypeName { get; set; }
    public string? PuzzleTypeCode { get; set; }
    public int AttemptTimeLimitMs { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string CreatedByEmail { get; set; } = string.Empty;
    public string CreatedByCode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int EventsCount { get; set; }
    public List<AdminTournamentEventDto> Events { get; set; } = new();
}

public class AdminTournamentPagedResultDto
{
    public List<AdminTournamentDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class UpdateTournamentStatusRequestDto
{
    public string StatusCode { get; set; } = string.Empty;
}
