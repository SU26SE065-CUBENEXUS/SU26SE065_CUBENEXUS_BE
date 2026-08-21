using System.Text.Json.Serialization;

namespace CubeNexus.Application.DTOs.FaceVerification;

public class FaceChallengeDto
{
    public string ChallengeId { get; set; } = string.Empty;
    public List<string> Actions { get; set; } = [];
}

public class FaceSessionStartResponseDto
{
    public Guid SessionId { get; set; }
    public string ExternalSessionId { get; set; } = string.Empty;
    public string UploadToken { get; set; } = string.Empty;
    public FaceChallengeDto Challenge { get; set; } = new();
    public DateTime ExpiresAt { get; set; }
    public string State { get; set; } = "POSITIONING";
    public string Purpose { get; set; } = string.Empty;
    public string ContextType { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string? PlayerName { get; set; }
    public Guid? RegistrationId { get; set; }
    public Guid? TournamentId { get; set; }
    public bool FaceEnrolled { get; set; }
}

public class FaceSessionStatusDto
{
    public Guid SessionId { get; set; }
    public string ExternalSessionId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string ContextType { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid? RegistrationId { get; set; }
    public FaceChallengeDto? Challenge { get; set; }
    public DateTime ExpiresAt { get; set; }
    public object? Result { get; set; }
    public string? FailureReason { get; set; }
    public bool? LivenessPassed { get; set; }
    public bool? FaceMatched { get; set; }
    public double? Similarity { get; set; }
}

public class FaceEnrollmentStatusDto
{
    public Guid UserId { get; set; }
    public bool IsEnrolled { get; set; }
    public string? Status { get; set; }
    public string? ModelVersion { get; set; }
    public double? QualityScore { get; set; }
    public int TemplatesCount { get; set; }
    public DateTime? EnrolledAt { get; set; }
}

public class StartCheckInFaceRequestDto
{
    public string QrToken { get; set; } = string.Empty;
}

public class StartCompetitorCheckInFaceRequestDto
{
    public Guid TournamentId { get; set; }
}

public class FaceCallbackRequestDto
{
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public object? Result { get; set; }
}

// ---- FastAPI wire models ----

public class FaceAiCreateSessionRequest
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("callbackUrl")]
    public string? CallbackUrl { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

public class FaceAiCreateSessionResponse
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("uploadToken")]
    public string UploadToken { get; set; } = string.Empty;

    [JsonPropertyName("challenge")]
    public FaceAiChallengeResponse? Challenge { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
}

public class FaceAiChallengeResponse
{
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = string.Empty;

    [JsonPropertyName("actions")]
    public List<string> Actions { get; set; } = [];
}

public class FaceAiSessionResultResponse
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public Dictionary<string, object?>? Result { get; set; }
}
