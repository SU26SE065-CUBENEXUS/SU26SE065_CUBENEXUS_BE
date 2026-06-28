namespace CubeNexus.Application.DTOs.OnlineArena;

public class CubeScanValidationRequest
{
    public Dictionary<string, List<List<string>>> CubeState { get; set; } = [];
    public CubeScanMetadataDto ScanMetadata { get; set; } = new();
}

public class CubeScanStickerMismatchDto
{
    public string Face { get; set; } = string.Empty;
    public int Row { get; set; }
    public int Column { get; set; }
    public string Expected { get; set; } = string.Empty;
    public string Observed { get; set; } = string.Empty;
}

public class OnlineArenaScannerAcceptedFaceDto
{
    public int FaceIndex { get; set; }
    public string FaceCode { get; set; } = string.Empty;
    public string ExpectedCenterColor { get; set; } = string.Empty;
    public string? ObservedCenterColor { get; set; }
    public List<List<string>>? Grid3x3 { get; set; }
    public DateTime AcceptedAt { get; set; }
}

public class OnlineArenaScannerValidationDto
{
    public string Status { get; set; } = string.Empty;
    public bool Matched { get; set; }
    public int MatchedStickerCount { get; set; }
    public int MismatchedStickerCount { get; set; }
    public string PlayerStatus { get; set; } = string.Empty;
    public List<CubeScanStickerMismatchDto> Mismatches { get; set; } = [];
}

public class OnlineArenaScannerSessionResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public string ValidationType { get; set; } = string.Empty;
    public string ScanSessionId { get; set; } = string.Empty;
    public string AiSessionId { get; set; } = string.Empty;
    public int ScanGeneration { get; set; }
    public string ScanStatus { get; set; } = string.Empty;
    public string ScannerState { get; set; } = string.Empty;
    public string MatchStatus { get; set; } = string.Empty;
    public int RequestedFaceIndex { get; set; }
    public string RequestedFaceCode { get; set; } = string.Empty;
    public string RequestedFaceLabel { get; set; } = string.Empty;
    public string RequestedCenterColor { get; set; } = string.Empty;
    public int CapturedFaceCount { get; set; }
    public string? RequestId { get; set; }
    public int StableObservationCount { get; set; }
    public int RequiredStableObservations { get; set; }
    public int DetectedStickers { get; set; }
    public double Confidence { get; set; }
    public double InferMs { get; set; }
    public double DecodeMs { get; set; }
    public double PreprocessMs { get; set; }
    public double PostprocessMs { get; set; }
    public double TotalMs { get; set; }
    public string? Reason { get; set; }
    public string? ObservedCenterColor { get; set; }
    public List<List<string>>? Grid3x3 { get; set; }
    public List<OnlineArenaScannerAcceptedFaceDto> Faces { get; set; } = [];
    public OnlineArenaScannerValidationDto? Validation { get; set; }
}

public class CubeScanMetadataDto
{
    public string ScannerVersion { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public string Runtime { get; set; } = "onnxruntime-web";
    public string ExecutionProvider { get; set; } = string.Empty;
    public double OverallConfidence { get; set; }
    public int ValidFrames { get; set; }
    public int DurationMs { get; set; }
    public string DeviceLabel { get; set; } = string.Empty;
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
}

public class CubeScanValidationResponseDto
{
    public string Message { get; set; } = string.Empty;
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public string ValidationType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string MatchStatus { get; set; } = string.Empty;
    public bool IsValidCubeState { get; set; }
    public bool? IsScrambleMatched { get; set; }
    public bool? IsSolved { get; set; }
    public string? Reason { get; set; }
    public List<string> Missing { get; set; } = [];
    public Dictionary<string, int> ColorCounts { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}
