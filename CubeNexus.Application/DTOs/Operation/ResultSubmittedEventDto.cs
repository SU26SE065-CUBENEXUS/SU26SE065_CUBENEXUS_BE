using System;

namespace CubeNexus.Application.DTOs.Operation;

public class ResultSubmittedEventDto
{
    public Guid EventId { get; set; }
    public int RoundNumber { get; set; }
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public Guid GroupCompetitorId { get; set; }
    public string CompetitorName { get; set; } = string.Empty;
    public int? StationNumber { get; set; }
    public string CompetitorStatus { get; set; } = string.Empty;
    public SubmittedResultDto Result { get; set; } = null!;
    public SubmittedResultSummaryDto Summary { get; set; } = null!;
}

public class SubmittedResultDto
{
    public Guid ResultId { get; set; }
    public int SolveNumber { get; set; }
    public int? RawTimeMs { get; set; }
    public int? FinalTimeMs { get; set; }
    public string PenaltyCode { get; set; } = string.Empty;
    public bool IsDnf { get; set; }
    public bool IsLocked { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class SubmittedResultSummaryDto
{
    public int CompletedSolves { get; set; }
    public int SolveCount { get; set; }
    public int? BestTimeMs { get; set; }
    public int? AverageTimeMs { get; set; }
    public int? Rank { get; set; }
}
