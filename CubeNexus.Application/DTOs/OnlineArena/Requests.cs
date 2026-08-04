namespace CubeNexus.Application.DTOs.OnlineArena;

public class InitOnlineProfileRequest
{
    public Guid PuzzleTypeId { get; set; }
}

public class FindMatchRequest
{
    public Guid PuzzleTypeId { get; set; }
}

public class ConnectMobileTimerRequest
{
    public string QrSessionCode { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}

public class DisconnectMobileTimerRequest
{
    public Guid MatchId { get; set; }
}

public class SubmitOnlineResultRequest
{
    public Guid MatchId { get; set; }
    public int? TimeMs { get; set; }
    public bool IsDnf { get; set; }
}

public class SubmitSolveTimeRequest
{
    public Guid MatchId { get; set; }
    public Guid MobileTimerSessionId { get; set; }
    public string DeviceSessionToken { get; set; } = string.Empty;
    public int? TimeMs { get; set; }
    public bool IsDnf { get; set; }
    public DateTime StoppedAt { get; set; }
}

public class OnlineArenaScannerObserveRequest
{
    public string ScanSessionId { get; set; } = string.Empty;
    public int ScanGeneration { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public int TargetFaceIndex { get; set; }
}

public class CreateFraudReportRequest
{
    public string FraudType { get; set; } = "OTHER";
    public string TimestampText { get; set; } = "00:00";
    public int TimestampSeconds { get; set; } = 0;
    public string Description { get; set; } = string.Empty;
    public string? EvidenceUrl { get; set; }
    public string? EvidenceScreenshotUrl { get; set; }
}

public class ReviewFraudReportRequest
{
    public string VerdictCode { get; set; } = string.Empty;
    public string? AdminNote { get; set; }
}

public class MarkWebRtcConnectedRequest
{
    public string ConnectionState { get; set; } = string.Empty;
    public string IceConnectionState { get; set; } = string.Empty;
}

public class MarkVideoRecordingStartedRequest
{
    public DateTime RecordingStartedAt { get; set; }
    public string? MimeType { get; set; }
}

public class ResolveFraudReportRequest
{
    public string Decision { get; set; } = string.Empty;
    public string? PenaltyAction { get; set; }
    public Guid? CheaterUserId { get; set; }
    public string? AdminNote { get; set; }
}
