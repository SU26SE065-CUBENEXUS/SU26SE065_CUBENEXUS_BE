using System.ComponentModel.DataAnnotations;

namespace CubeNexus.Application.DTOs;

public class CreateOnlineAsyncTournamentRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public Guid PuzzleTypeId { get; set; }

    [Required]
    public DateTime RegistrationOpenAt { get; set; }

    [Required]
    public DateTime RegistrationCloseAt { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    [Required]
    public DateTime EndDate { get; set; }

    public int AttemptTimeLimitMs { get; set; } = 300000; // 5 minutes default
}

public class OnlineAsyncTournamentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TournamentType { get; set; } = "ONLINE_ASYNC";
    public string FormatCode { get; set; } = "AO1";
    public Guid? PuzzleTypeId { get; set; }
    public string? PuzzleTypeName { get; set; }
    public string? ScrambleSequence { get; set; }
    public int AttemptTimeLimitMs { get; set; } = 300000;
    public DateTime RegistrationOpenAt { get; set; }
    public DateTime RegistrationCloseAt { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // User contextual state
    public bool IsRegistered { get; set; } = false;
    public string? UserAttemptStatus { get; set; }
    public Guid? UserAttemptId { get; set; }
}

public class StartOnlineAsyncAttemptResponse
{
    public Guid AttemptId { get; set; }
    public Guid TournamentId { get; set; }
    public string ScrambleSequence { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public int TimeLimitMs { get; set; } = 300000;
    public string Status { get; set; } = "INITIALIZED";
}

public class VerifyAsyncScrambleRequest
{
    public Guid AttemptId { get; set; }
    public string? ScrambleSequence { get; set; }
    public List<ScrambleCheckFaceDto>? Faces { get; set; }
    /// <summary>JPEG/PNG snapshot captured from the competitor camera for AI verification.</summary>
    public string? ImageBase64 { get; set; }
}

public class ScrambleCheckFaceDto
{
    public string Face { get; set; } = string.Empty; // U, D, L, R, F, B
    public List<List<string>> Grid { get; set; } = [];
}

public class VerifyAsyncScrambleResponse
{
    public Guid AttemptId { get; set; }
    public bool Passed { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime? AttemptDeadlineAt { get; set; }
    public DateTime? HandTimerStartedAt { get; set; }
}

public class StartAsyncSolveTimerRequest
{
    public Guid AttemptId { get; set; }

    /// <summary>
    /// Client-observed duration, retained as telemetry/API compatibility only.
    /// The server calculates the authoritative duration from HandTimerStartedAt:
    ///  0 .. 6000 ms -> Normal; 6001 .. 14000 ms -> +2s; > 14000 ms -> DNF.
    /// </summary>
    public int HandTimerMs { get; set; }
}

public class StartAsyncSolveTimerResponse
{
    public Guid AttemptId { get; set; }
    public string Status { get; set; } = "SOLVING";
    public DateTime SolveStartedAt { get; set; }
    public int HandTimerMs { get; set; }
    public string PenaltyCode { get; set; } = "NONE"; // NONE | PLUS2 | DNF
    public int PenaltyTimeMs { get; set; } = 0;
    public bool IsDnf { get; set; } = false;
    public string Message { get; set; } = string.Empty;
}

public class FinishAsyncSolveTimerRequest
{
    public Guid AttemptId { get; set; }
    public int RawTimeMs { get; set; }
}

public class FinishAsyncSolveTimerResponse
{
    public Guid AttemptId { get; set; }
    public int RawTimeMs { get; set; }
    public int PenaltyTimeMs { get; set; }
    public string PenaltyCode { get; set; } = "NONE";
    public bool IsDnf { get; set; }
    public int? FinalTimeMs { get; set; }
    public string Status { get; set; } = "COMPLETED";
    public string ReviewStatus { get; set; } = "PENDING_REVIEW";
    public string DisplayResult { get; set; } = string.Empty;
}

public class OnlineAsyncAttemptStateDto : FinishAsyncSolveTimerResponse
{
    public Guid TournamentId { get; set; }
    public string AttemptStatus { get; set; } = "INITIALIZED";
    public string ScrambleCheckStatus { get; set; } = "PENDING";
    public string FinishCheckStatus { get; set; } = "PENDING";
    public DateTime? AttemptDeadlineAt { get; set; }
    public DateTime? HandTimerStartedAt { get; set; }
    public DateTime? SolveStartedAt { get; set; }
    public string ScrambleSequence { get; set; } = string.Empty;
}

public class VerifyAsyncFinishRequest
{
    public Guid AttemptId { get; set; }
    public List<ScrambleCheckFaceDto>? Faces { get; set; }
    public string? VideoUrl { get; set; }
    public string? ImageBase64 { get; set; }
}

public class AsyncAttemptVideoUploadResponse
{
    public Guid AttemptId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string RecordingStatus { get; set; } = "READY";
}

public class CreateAsyncAttemptVideoUploadUrlRequest
{
    public string ContentType { get; set; } = "video/webm";
    public string FileExtension { get; set; } = "webm";
}

public class AsyncAttemptVideoUploadUrlResponse
{
    public Guid AttemptId { get; set; }
    public string UploadUrl { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "video/webm";
    public DateTime ExpiresAt { get; set; }
}

public class CompleteAsyncAttemptVideoUploadRequest
{
    public string ObjectKey { get; set; } = string.Empty;
}

public class ReviewAsyncAttemptRequest
{
    public Guid AttemptId { get; set; }

    /// <summary>
    /// APPROVED | REJECTED
    /// </summary>
    [Required]
    public string ReviewStatus { get; set; } = "APPROVED";

    /// <summary>
    /// NONE | PLUS2 | DNF
    /// </summary>
    public string PenaltyCode { get; set; } = "NONE";

    public string? ReviewNote { get; set; }
}

public class AsyncLeaderboardEntryDto
{
    public int Rank { get; set; }
    public Guid AttemptId { get; set; }
    public Guid UserId { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string? UserAvatarUrl { get; set; }
    public int? RawTimeMs { get; set; }
    public int PenaltyTimeMs { get; set; }
    public string PenaltyCode { get; set; } = "NONE";
    public bool IsDnf { get; set; }
    public int? FinalTimeMs { get; set; }
    public string DisplayResult { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = "APPROVED";
    public string? VideoEvidenceUrl { get; set; }
    public DateTime SolveFinishedAt { get; set; }
}
