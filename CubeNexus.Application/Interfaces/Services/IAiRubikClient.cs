using CubeNexus.Application.DTOs.OnlineArena;

namespace CubeNexus.Application.Interfaces.Services;

public interface IAiRubikClient
{
    Task<AiRubikHealthDto?> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<AiRubikCheckResultDto> PreCheckAsync(AiRubikCheckRequestDto request, CancellationToken cancellationToken = default);
    Task<AiRubikCheckResultDto> ScrambleCheckAsync(AiRubikCheckRequestDto request, CancellationToken cancellationToken = default);
    Task<AiRubikCheckResultDto> FinishCheckAsync(AiRubikCheckRequestDto request, CancellationToken cancellationToken = default);
    Task<AiRubikScannerSessionDto> StartScannerTestSessionAsync(CancellationToken cancellationToken = default);
    Task<AiRubikScannerSessionDto> GetScannerTestSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<AiRubikScannerPreviewDto> PreviewScannerTestFrameAsync(string sessionId, string imageBase64, Dictionary<string, object?> metadata, CancellationToken cancellationToken = default);
    Task<AiRubikScannerPreviewDto> ObserveScannerTestFrameAsync(string sessionId, string imageBase64, Dictionary<string, object?> metadata, CancellationToken cancellationToken = default);
    Task<AiRubikScannerSessionDto> ScanScannerTestFaceAsync(string sessionId, IReadOnlyCollection<string> framesBase64, CancellationToken cancellationToken = default);
    Task<AiRubikScannerSessionDto> RetryScannerTestFaceAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<AiRubikScannerSessionDto> ResetScannerTestSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
