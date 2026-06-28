namespace CubeNexus.Application.DTOs.OnlineArena;

public class AiRubikCheckRequestDto
{
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public string CheckType { get; set; } = string.Empty;
    public string? ScrambleSequence { get; set; }
    public string? ImageBase64 { get; set; }
    public string? ImageUrl { get; set; }
    public Dictionary<string, object?> Metadata { get; set; } = [];
}

public class AiRubikCheckResultDto
{
    public string CheckType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool DetectedCube { get; set; }
    public int DetectedStickers { get; set; }
    public List<List<string>>? Grid3x3 { get; set; }
    public string? Reason { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public bool ModelLoaded { get; set; }
    public string? ExpectedScramble { get; set; }
    public string? DetectedState { get; set; }
    public bool? IsScrambleMatched { get; set; }
    public bool? IsSolved { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AiRubikHealthDto
{
    public string Status { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public bool ModelExists { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public bool ModelLoaded { get; set; }
}

public class AiRubikScannerStickerDto
{
    public string Color { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<int> Bbox { get; set; } = [];
}

public class AiRubikScannerFaceDto
{
    public string CenterColor { get; set; } = string.Empty;
    public List<List<string>> Grid3x3 { get; set; } = [];
    public List<AiRubikScannerStickerDto> Stickers { get; set; } = [];
    public double OverallConfidence { get; set; }
    public int ValidFrames { get; set; }
    public DateTime CapturedAt { get; set; }
}

public class AiRubikScannerPreviewDto
{
    public string Status { get; set; } = string.Empty;
    public string ScannerState { get; set; } = string.Empty;
    public string ScanSessionId { get; set; } = string.Empty;
    public int ScanGeneration { get; set; }
    public string? RequestId { get; set; }
    public int TargetFaceIndex { get; set; }
    public int RequestedFaceIndex { get; set; }
    public string RequestedFaceLabel { get; set; } = string.Empty;
    public string? CenterColor { get; set; }
    public List<List<string>>? Grid3x3 { get; set; }
    public List<AiRubikScannerStickerDto> Stickers { get; set; } = [];
    public int DetectedStickers { get; set; }
    public double Confidence { get; set; }
    public double InferMs { get; set; }
    public double DecodeMs { get; set; }
    public double PreprocessMs { get; set; }
    public double PostprocessMs { get; set; }
    public double TotalMs { get; set; }
    public int StableObservationCount { get; set; }
    public int RequiredStableObservations { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class AiRubikScannerSessionDto
{
    public string SessionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ScannerState { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int ScanGeneration { get; set; }
    public int RequestedFaceIndex { get; set; }
    public string RequestedFaceLabel { get; set; } = string.Empty;
    public int CapturedFaceCount { get; set; }
    public int RawStickerCount { get; set; }
    public bool OrientationResolved { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<AiRubikScannerFaceDto> Faces { get; set; } = [];
    public List<string> RawStickerState { get; set; } = [];
    public AiRubikScannerFaceDto? LastFaceScan { get; set; }
    public string? LastScanStatus { get; set; }
    public string? LastScanReason { get; set; }
}
