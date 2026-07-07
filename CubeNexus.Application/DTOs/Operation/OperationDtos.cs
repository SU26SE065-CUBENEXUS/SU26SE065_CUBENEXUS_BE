using System;
using System.Collections.Generic;

namespace CubeNexus.Application.DTOs.Operation;

public class GenerateGroupsDto
{
    public int RoundNumber { get; set; } = 1;
    public int CompetitorsPerGroup { get; set; } = 8;
    public int StationCount { get; set; } = 4;
}

public class GenerateScramblesDto
{
    public int RoundNumber { get; set; } = 1;
}

public class SubmitTraditionalResultDto
{
    public Guid GroupCompetitorId { get; set; }
    public int SolveNumber { get; set; }
    public int? RawTimeMs { get; set; }
    public Guid? PenaltyTypeId { get; set; }
    public Guid ScrambleId { get; set; }
    public string? EsignatureData { get; set; }
}

public class SubmitMedleyResultDto
{
    public Guid GroupCompetitorId { get; set; }
    public int SolveNumber { get; set; }
    public string? EsignatureData { get; set; }
    public List<MedleyDetailSubmissionDto> Details { get; set; } = new();
}

public class MedleyDetailSubmissionDto
{
    public Guid MedleyPuzzleId { get; set; }
    public int? RawTimeMs { get; set; }
    public Guid? PenaltyTypeId { get; set; }
    public Guid ScrambleId { get; set; }
}

public class OperationResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SubmitResultResponseDto
{
    public Guid ResultId { get; set; }
    public int? FinalTimeMs { get; set; }
    public bool IsDnf { get; set; }
    public int? SubmittedSolveNumber { get; set; }
    public SubmitProgressDto? Progress { get; set; }
    public ScrambleInfoDto? NextScramble { get; set; }
}

public class ScrambleInfoDto
{
    public Guid ScrambleId { get; set; }
    public int SolveNumber { get; set; }
    public string Sequence { get; set; } = string.Empty;
}

public class SubmitProgressDto
{
    public int SubmittedCount { get; set; }
    public int SolveCount { get; set; }
    public int? NextSolveNumber { get; set; }
    public bool CanSubmitNext { get; set; }
}

public class SolveProgressDto
{
    public Guid GroupCompetitorId { get; set; }
    public Guid? EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public int? RoundNumber { get; set; }
    public Guid? GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int? StationNumber { get; set; }
    public int SolveCount { get; set; }
    public List<int> SubmittedSolveNumbers { get; set; } = new();
    public int SubmittedCount { get; set; }
    public int? NextSolveNumber { get; set; }
    public bool CanSubmit { get; set; }
    public string? Reason { get; set; }
    public ScrambleInfoDto? CurrentScramble { get; set; }
}


public class GroupDto
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public int RoundNumber { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public List<GroupCompetitorDto> Competitors { get; set; } = new();
}

public class GroupCompetitorDto
{
    public Guid Id { get; set; }
    public Guid RegistrationEventId { get; set; }
    public string CompetitorName { get; set; } = string.Empty;
    public int? StationNumber { get; set; }
}

public class AdvanceRoundRequestDto
{
    public int NextRoundNumber { get; set; }
    public int TopN { get; set; }
    public int CompetitorsPerGroup { get; set; }
    public int StationCount { get; set; }
}


public class VerifyJudgeStationByStationDto
{
    public string QrToken { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public int RoundNumber { get; set; }
    public int GroupNumber { get; set; }
    public int StationNumber { get; set; }
}

public class VerifyJudgeStationResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    
    public Guid? GroupCompetitorId { get; set; }
    public Guid? EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public int? RoundNumber { get; set; }
    public Guid? GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string CompetitorName { get; set; } = string.Empty;
    public int? StationNumber { get; set; }
    public int? NextSolveNumber { get; set; }
    public int? SolveCount { get; set; }
    public bool CanSubmit { get; set; }
    public ScrambleInfoDto? CurrentScramble { get; set; }
}

public class JudgeStationRosterItemDto
{
    public Guid GroupCompetitorId { get; set; }
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string CompetitorName { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public int StationNumber { get; set; }
    public int SolveCount { get; set; }
    public int SubmittedCount { get; set; }
    public int? NextSolveNumber { get; set; }
    public bool CanSubmit { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class JudgeStationRosterResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<JudgeStationRosterItemDto> Competitors { get; set; } = new();
}

public class ResultCorrectionDto
{
    public int? RawTimeMs { get; set; }
    public Guid? PenaltyTypeId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ResultCorrectionResponseDto
{
    public Guid ResultId { get; set; }
    public int? RawTimeMs { get; set; }
    public int? FinalTimeMs { get; set; }
    public string PenaltyCode { get; set; } = string.Empty;
    public bool IsDnf { get; set; }
    public bool IsLocked { get; set; }
    public DateTime CorrectedAt { get; set; }
    public Guid CorrectedBy { get; set; }
    public string CorrectionReason { get; set; } = string.Empty;

    // Competitor summary
    public int CompletedSolves { get; set; }
    public int SolveCount { get; set; }
    public int? BestTimeMs { get; set; }
    public int? AverageTimeMs { get; set; }
    public int Rank { get; set; }
    public string CompetitorStatus { get; set; } = string.Empty;
}
