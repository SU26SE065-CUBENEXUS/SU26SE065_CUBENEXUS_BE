using System;
using System.Collections.Generic;
using CubeNexus.Application.DTOs.Tournament;

namespace CubeNexus.Application.DTOs.PublicLive;

public class PublicLiveTournamentDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsLive { get; set; }

    public List<PublicLiveEventDto> Events { get; set; } = new();
    
    public Guid? ActiveEventId { get; set; }
    public int? ActiveRoundNumber { get; set; }
}

public class PublicLiveEventDto
{
    public Guid Id { get; set; }
    public Guid PuzzleTypeId { get; set; }
    public string PuzzleTypeName { get; set; } = string.Empty;
    public string PuzzleTypeCode { get; set; } = string.Empty;
    public string EventFormatCode { get; set; } = string.Empty;
    public int SolveCount { get; set; }
    public int? SortOrder { get; set; }
    public int? TimeLimitMs { get; set; }
    public int? CutoffTimeMs { get; set; }
    
    public int? CurrentRoundNumber { get; set; }
    public string? RoundStatus { get; set; } // ONGOING, COMPLETED, LOCKED, PENDING, or null
    
    public List<MedleyPuzzleDetailDto> MedleyPuzzles { get; set; } = new();
}
