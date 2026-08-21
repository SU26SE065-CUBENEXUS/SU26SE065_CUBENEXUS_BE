using CubeNexus.Application.DTOs.FaceVerification;

namespace CubeNexus.Application.Interfaces.Services;

public interface IFaceVerificationClient
{
    Task<FaceAiCreateSessionResponse> CreateEnrollmentSessionAsync(
        FaceAiCreateSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<FaceAiCreateSessionResponse> CreateVerificationSessionAsync(
        FaceAiCreateSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<FaceAiSessionResultResponse> SubmitEnrollmentEvidenceAsync(
        string externalSessionId,
        string uploadToken,
        string metadataJson,
        Stream? evidenceVideo,
        string? evidenceVideoFileName,
        string? evidenceVideoContentType,
        IReadOnlyList<(Stream Content, string FileName, string? ContentType)> images,
        CancellationToken cancellationToken = default);

    Task<FaceAiSessionResultResponse> SubmitPassiveEvidenceAsync(
        string externalSessionId,
        string uploadToken,
        IReadOnlyList<(Stream Content, string FileName, string? ContentType)> finalFrames,
        CancellationToken cancellationToken = default);

    Task<FaceAiSessionResultResponse> SubmitActiveEvidenceAsync(
        string externalSessionId,
        string uploadToken,
        string metadataJson,
        Stream? evidenceVideo,
        string? evidenceVideoFileName,
        string? evidenceVideoContentType,
        IReadOnlyList<(Stream Content, string FileName, string? ContentType)> finalFrames,
        CancellationToken cancellationToken = default);

    Task<object?> AnalyzeFrameAsync(Stream frame, string fileName, string? contentType, CancellationToken cancellationToken = default);

    Task<object?> GetHealthAsync(CancellationToken cancellationToken = default);
}
