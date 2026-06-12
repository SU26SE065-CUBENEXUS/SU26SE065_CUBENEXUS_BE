using System;

namespace CubeNexus.Application.DTOs.Operation;

public class ResultCorrectedEventDto
{
    public Guid EventId { get; set; }
    public int RoundNumber { get; set; }
    public Guid GroupId { get; set; }
    public Guid GroupCompetitorId { get; set; }
    public string CompetitorName { get; set; } = string.Empty;
    public CorrectedResultDto Result { get; set; } = null!;
    public CorrectedResultSummaryDto Summary { get; set; } = null!;
}

public class CorrectedResultDto
{
    public Guid ResultId { get; set; }
    public int SolveNumber { get; set; }
    public int? RawTimeMs { get; set; }
    public int? FinalTimeMs { get; set; }
    public string PenaltyCode { get; set; } = string.Empty;
    public bool IsDnf { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? CorrectedAt { get; set; }
}

public class CorrectedResultSummaryDto
{
    public int CompletedSolves { get; set; }
    public int SolveCount { get; set; }
    public int? BestTimeMs { get; set; }
    public int? AverageTimeMs { get; set; }
    public int? Rank { get; set; }
}
