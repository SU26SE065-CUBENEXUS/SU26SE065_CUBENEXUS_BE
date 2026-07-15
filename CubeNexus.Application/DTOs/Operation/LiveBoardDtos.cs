using System;
using System.Collections.Generic;

namespace CubeNexus.Application.DTOs.Operation;

public class LiveBoardStateDto
{
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public string RoundStatus { get; set; } = string.Empty;
    public int SolveCount { get; set; }
    public LiveBoardProgressDto Progress { get; set; } = null!;
    public List<LiveBoardGroupDto> Groups { get; set; } = new();
    public List<LiveBoardCompetitorDto> Competitors { get; set; } = new();
}

public class LiveBoardGroupDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
}

public class LiveBoardCompetitorDto
{
    public Guid GroupCompetitorId { get; set; }
    public string CompetitorName { get; set; } = string.Empty;
    public string CompetitorUserCode { get; set; } = string.Empty;
    public string? CompetitorAvatarUrl { get; set; }
    public int? StationNumber { get; set; }
    public string CompetitorStatus { get; set; } = string.Empty;
    public Guid GroupId { get; set; }
    public List<LiveBoardResultDto> Results { get; set; } = new();
    public int? BestTimeMs { get; set; }
    public int? AverageTimeMs { get; set; }
    public int? Rank { get; set; }
    public int CompletedSolves { get; set; }
}

public class LiveBoardResultDto
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

public class LiveBoardProgressDto
{
    public int TotalCompetitors { get; set; }
    public int CompletedCompetitors { get; set; }
    public int NoShowCompetitors { get; set; }
    public int PendingCompetitors { get; set; }
    public int TotalExpectedSolves { get; set; }
    public int SubmittedSolves { get; set; }
}

public class LiveBoardRankingDto
{
    public int? Rank { get; set; }
    public Guid GroupCompetitorId { get; set; }
    public string CompetitorName { get; set; } = string.Empty;
    public int? BestTimeMs { get; set; }
    public int? AverageTimeMs { get; set; }
    public int CompletedSolves { get; set; }
    public string CompetitorStatus { get; set; } = string.Empty;
}

public class ResultsLockedEventDto
{
    public Guid EventId { get; set; }
    public int RoundNumber { get; set; }
    public DateTime LockedAt { get; set; }
}

public class RoundCompletedEventDto
{
    public Guid EventId { get; set; }
    public int RoundNumber { get; set; }
    public string RoundStatus { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
}
